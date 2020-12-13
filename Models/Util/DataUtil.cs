﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Glimmr.Models.LED;
using Glimmr.Models.StreamingDevice.Dreamscreen;
using Glimmr.Models.StreamingDevice.Hue;
using Glimmr.Models.StreamingDevice.LIFX;
using Glimmr.Models.StreamingDevice.Nanoleaf;
using Glimmr.Models.StreamingDevice.WLED;
using Glimmr.Services;
using LifxNet;
using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Glimmr.Models.Util {
    [Serializable]
    public static class DataUtil {
        public static bool Scanning { get; set; }
        private static LiteDatabase _db;
        
        public static LiteDatabase GetDb() {
            if (_db == null) _db = new LiteDatabase(@"./store.db");
            return _db;
        }

        public static void Dispose() {
            Log.Debug("DISPOSING DATABASE.");
            _db?.Commit();
            _db?.Dispose();
        }

        public static void CheckDefaults(LifxClient lc) {
            var db = GetDb();
            // Check to see if we have our system data object
            var defaultSet = GetItem("DefaultSet");
            if (defaultSet == null || defaultSet == false) {
                Log.Warning("Setting default values!");
                // If not, create it
                var sd = new SystemData(true);
                foreach (var v in sd.GetType().GetProperties()) {
                    Log.Debug("Setting: " + v.Name);
                    SetItem(v.Name, v.GetValue(sd));
                }

                var deviceIp = IpUtil.GetLocalIpAddress();
                var ledData = new LedData();
                var myDevice = new DreamData {Id = deviceIp, IpAddress = deviceIp};
                Log.Debug("Creating default device data: " + JsonConvert.SerializeObject(myDevice));
                SetObject("LedData", ledData);
                SetObject("MyDevice", myDevice);
                Log.Debug("Creating DreamData profile...");
                // Get/create our collection of Dream devices
                var d = db.GetCollection<DreamData>("Dev_Dreamscreen");
                // Create our default device
                // Save it
                d.Upsert(myDevice.Id, myDevice);
                d.EnsureIndex(x => x.Id);
                db.Commit();
                // Scan for devices
                ScanDevices(lc).ConfigureAwait(false);
            } else {
                Log.Information("Default values are set, continuing.");
            }
        }
       
        //fixed
        public static List<dynamic> GetCollection(string key) {
            try {
                var db = GetDb();
                var coll = db.GetCollection(key);
                var output = new List<dynamic>();
                if (coll == null) return output;
                output.AddRange(coll.FindAll());
                return output;
            } catch (Exception e) {
                Log.Warning($@"Get exception for {key}:", e);
                return null;
            }
        }
        //fixed
        public static List<T> GetCollection<T>() where T : class {
            try {
                var db = GetDb();
                var coll = db.GetCollection<T>();
                var output = new List<T>();
                if (coll == null) return output;
                output.AddRange(coll.FindAll());
                return output;
            } catch (Exception e) {
                Log.Debug($@"Get exception for {typeof(T)}: {e.Message}");
                return null;
            }
        }
        //fixed
        public static List<T> GetCollection<T>(string key) where T : class {

            var db = GetDb();
            var coll = db.GetCollection<T>(key);
            var output = new List<T>();
            if (coll == null) return output;
            output.AddRange(coll.FindAll());
            return output;
            
        }
        //fixed
        public static dynamic GetCollectionItem<T>(string key, string value) where T : new() {
            try {
                var db = GetDb();
                var coll = db.GetCollection<T>(key);
                    var r = coll.FindById(value);
                    return r;
                
            } catch (Exception e) {
                Log.Debug($@"Get exception for {typeof(T)}: {e.Message}");
                return null;
            }
        }
        //fixed
        public static void InsertCollection<T>(string key, dynamic value) where T: class {
            var db = GetDb();
            var coll = db.GetCollection<T>(key);
            coll.Upsert(value.Id, value);
            db.Commit();
        }
        //fixed
        public static void InsertCollection(string key, dynamic value) {
                var db = GetDb();
                var coll = db.GetCollection(key);
                coll.Upsert(value.Id, value);
                db.Commit();
        }


        
        
        public static string GetDeviceSerial() {
            var serial = string.Empty;
            try {
                serial = GetItem("Serial");
            } catch (KeyNotFoundException) {

            }

            if (string.IsNullOrEmpty(serial)) {
                Random rd = new Random();
                serial = "12091" + rd.Next(0, 9) + rd.Next(0, 9) + rd.Next(0, 9);
                SetItem("Serial", serial);
            }

            return serial;
        }


        public static dynamic GetDeviceById(string id) {
            var db = GetDb();
            try {
                var coll = db.GetCollection<HueData>("Dev_Hue");
                var bDev = coll.FindById(id);
                if (bDev != null) return bDev;

                var coll1 = db.GetCollection<LifxData>("Dev_Lifx");
                var bDev1 = coll1.FindById(id);
                if (bDev1 != null) return bDev1;

                var coll2 = db.GetCollection<NanoleafData>("Dev_Nanoleaf");
                var bDev2 = coll2.FindById(id);
                if (bDev2 != null) return bDev2;

                var coll3 = db.GetCollection<WledData>("Dev_Wled");
                var bDev3 = coll3.FindById(id);
                if (bDev3 != null) return bDev3;

                var coll4 = db.GetCollection<NanoleafData>("Dev_Dreamscreen");
                var bDev4 = coll4.FindById(id);
                if (bDev4 != null) return bDev4;
            } catch (Exception e) {
                Log.Debug("Exception getting device data");
            }

            return null;

        }

        
        public static string GetStoreSerialized() {
            var db = GetDb();
            var cols = db.GetCollectionNames();
            var output = new Dictionary<string, List<dynamic>>();
            foreach (var col in cols) {
                var collection = db.GetCollection(col);
                var list = collection.FindAll().ToList();
                var lList = new List<dynamic>();
                foreach (var l in list) {
                    var jObj = LiteDB.JsonSerializer.Serialize(l);
                    var json = JObject.Parse(jObj);
                    lList.Add(json);
                }
                output[col] = lList;
            }

            return JsonConvert.SerializeObject(output);
        }

        public static DreamData GetDeviceData() {
            try {
                var myDevice = GetObject<DreamData>("MyDevice");
                if (myDevice != null) return myDevice;
            } catch (Exception e) {
                Log.Warning("Exception fetching device data: ", e);
            }

            var devIp = IpUtil.GetLocalIpAddress();
            var newDevice = new DreamData {Id = devIp};
            SetObject("MyDevice", newDevice);
            return newDevice;
            
        }

        public static void SetItem<T>(string key, dynamic value) {
            var db = GetDb();
            var col = db.GetCollection(key);
            col.Upsert(0,new BsonDocument { ["value"] = value });
            db.Commit();
        }
        
        public static void SetInt(string key, int value) {
            var db = GetDb();
            var col = db.GetCollection(key);
            col.Upsert(0,new BsonDocument { ["value"] = value });
            db.Commit();
        }

        
        public static dynamic GetItem<T>(string key) {
            var i = GetItem(key);
            if (i == null) {
                return null;
            } 
            return (T) GetItem(key);
        }
        
        public static void SetItem(string key, dynamic value) {
            var db = GetDb();
            var col = db.GetCollection(key);
            col.Upsert(0, new BsonDocument { ["value"] = value });
            db.Commit();
        }
        
        public static void SetObject(string key, dynamic value) {
            var db = GetDb();
            var doc = BsonMapper.Global.ToDocument(value);
            var col = db.GetCollection(key);
            col.Upsert(0, doc);
            db.Commit();
        }

        public static dynamic GetItem(string key) {
            var db = GetDb();
            var col = db.GetCollection(key);
            foreach(var doc in col.FindAll()) {
                return doc["value"];
            }

            return null;
        }
        
        public static dynamic GetObject<T>(string key) {
            var db = GetDb();
            var col = db.GetCollection<T>(key);
            if (col.Count() == 0) return null;
            try {
                foreach (var doc in col.FindAll()) {
                    return doc;
                }
            } catch (Exception) {
                
            }

            return null;
        }

        
        
        
        
        public static List<DreamData> GetDreamDevices() {
            var dd = GetDb();
            var devs = dd.GetCollection<DreamData>("Dev_Dreamscreen");
            var dl = devs.FindAll();
            return dl.ToList();
        }

        public static DreamData GetDreamDevice(string id) {
            return GetDreamDevices().FirstOrDefault(dev => dev.Id == id);
        }

        public static (int, int) GetTargetLights() {
            var db = GetDb();
            var dsIp = GetItem("DsIp");
            var devices = db.GetCollection<DreamData>("Dev_Dreamscreen").FindAll();
            foreach (var dev in devices) {
                var tsIp = dev.IpAddress;
                Log.Debug("Device IP: " + tsIp);
                if (tsIp != dsIp) continue;
                Log.Debug("We have a matching IP");
                var fs = dev.FlexSetup;
                var dX = fs[0];
                var dY = fs[1];
                Log.Debug($@"DX, DY: {dX} {dY}");
                return (dX, dY);
            }

            return (0, 0);
        }

        /// <summary>
        ///     Determine if config path is local, or docker
        /// </summary>
        /// <param name="filePath">Config file to check</param>
        /// <returns>Modified path to config file</returns>
        private static string GetConfigPath(string filePath) {
            // If no etc dir, return normal path
            if (!Directory.Exists("/etc/glimmr")) return filePath;
            // Make our etc path for docker
            var newPath = "/etc/glimmr/" + filePath;
            // If the config file doesn't exist locally, we're done
            if (!File.Exists(filePath)) return newPath;
            // Otherwise, move the config to etc
            Log.Debug($@"Moving file from {filePath} to {newPath}");
            File.Copy(filePath, newPath);
            File.Delete(filePath);
            return newPath;
        }


        public static async void RefreshDevices(LifxClient c, ControlService controlService) {
            var cs = new CancellationTokenSource();
            cs.CancelAfter(30000);
            Log.Debug("Starting scan.");
            Scanning = true;
            // Get dream devices
            var ld = new LifxDiscovery(c);
            var nanoTask = NanoleafDiscovery.Refresh(cs.Token);
            var bridgeTask = HueDiscovery.Refresh(cs.Token);
            var wLedTask = WledDiscovery.Discover();
            var bulbTask = ld.Refresh(cs.Token);
            controlService.RefreshDreamscreen(cs.Token);
            try {
                await Task.WhenAll(nanoTask, bridgeTask, bulbTask, wLedTask);
            } catch (TaskCanceledException e) {
                Log.Warning("Discovery task was canceled before completion: ", e);
            } catch (SocketException f) {
                Log.Warning("Socket exception during discovery: ", f);
            }
				
            Log.Debug("Refresh complete.");
            try {
                var leaves = nanoTask.Result;
                var bridges = bridgeTask.Result;
                var bulbs = bulbTask.Result;
                var wleds = wLedTask.Result;
                var db = GetDb();
                var bridgeCol = db.GetCollection<HueData>("Dev_Hue");
                var nanoCol = db.GetCollection<NanoleafData>("Dev_Nanoleaf");
                var devCol = db.GetCollection<DreamData>("Dev_Dreamscreen");
                var lifxCol = db.GetCollection<LifxData>("Dev_Lifx");
                var wledCol = db.GetCollection<WledData>("Dev_Wled");
                foreach (var b in bridges) {
                    var nb = b;
                    if (b.Key !=  null && b.User != null) {
                        var n = new HueDevice(b);
                        nb = n.RefreshData().Result;
                        n.Dispose();
                    }
                    bridgeCol.Upsert(nb);
                }

                foreach (var b in bridgeCol.FindAll()) {
                    HueData nb;
                    if (b.Key !=  null && b.User != null) {
                        var n = new HueDevice(b);
                        nb = n.RefreshData().Result;
                        Log.Debug("Got me a bridge to update: " + nb.IpAddress);
                        bridgeCol.Upsert(nb);
                        n.Dispose();
                    }
                }
                foreach (var n in leaves) nanoCol.Upsert(n);
                foreach (var b in bulbs) lifxCol.Upsert(b);
                foreach (var w in wleds) wledCol.Upsert(w);
                bridgeCol.EnsureIndex(x => x.Id);
                nanoCol.EnsureIndex(x => x.Id);
                devCol.EnsureIndex(x => x.Id);
                lifxCol.EnsureIndex(x => x.Id);
                wledCol.EnsureIndex(x => x.Id);
                var dsCol = db.GetCollection<DreamData>("Dev_Dreamscreen");
                dsCol.EnsureIndex(x => x.Id);
            } catch (TaskCanceledException) {

            } catch (AggregateException) {
                
            }

            Scanning = false;
            cs.Dispose();
        }

        public static async Task ScanDevices(LifxClient lc) {
            if (Scanning) return;
            Scanning = true;
            var db = GetDb();
                // Get dream devices
                var ld = new LifxDiscovery(lc);
                var nanoTask = NanoleafDiscovery.Discover();
                var hueTask = HueDiscovery.Discover();
                var wLedTask = WledDiscovery.Discover();
                var bulbTask = ld.Discover(5);
                await Task.WhenAll(nanoTask, hueTask, bulbTask).ConfigureAwait(false);
                var leaves = await nanoTask.ConfigureAwait(false);
                var bridges = await hueTask.ConfigureAwait(false);
                var bulbs = await bulbTask.ConfigureAwait(false);
                var wleds = await wLedTask.ConfigureAwait(false);
                var bridgeCol = db.GetCollection<HueData>("Dev_Hue");
                var nanoCol = db.GetCollection<NanoleafData>("Dev_Nanoleaf");
                var devCol = db.GetCollection<DreamData>("Dev_Dreamscreen");
                var lifxCol = db.GetCollection<LifxData>("Dev_Lifx");
                var wledCol = db.GetCollection<WledData>("Dev_Wled");
                foreach (var b in bridges) bridgeCol.Upsert(b);
                foreach (var n in leaves) nanoCol.Upsert(n);
                foreach (var b in bulbs) lifxCol.Upsert(b);
                foreach (var w in wleds) wledCol.Upsert(w);
                bridgeCol.EnsureIndex(x => x.Id);
                nanoCol.EnsureIndex(x => x.Id);
                devCol.EnsureIndex(x => x.Id);
                lifxCol.EnsureIndex(x => x.Id);
                wledCol.EnsureIndex(x => x.Id);
                Scanning = false;
            
        }

        public static void RefreshPublicIp() {
            var myIp = Dns.GetHostEntry(Dns.GetHostName()).AddressList[0].ToString();
            Log.Debug("My IP Address is :" + myIp);
        }
    }
}