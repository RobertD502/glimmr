﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Glimmr.Models.Util;
using Makaretu.Dns;
using Serilog;

namespace Glimmr.Models.StreamingDevice.WLED {
    public static class WledDiscovery {
        private static List<WledData> _discovered;
        
        public static async Task<List<WledData>> Discover(int timeout = 5) {
            _discovered = new List<WledData>();
            var existing = new List<WledData>();
            try {
                existing = DataUtil.GetCollection<WledData>("Dev_Wled");
            } catch (Exception e) {
                Log.Debug("No Led data: " + e.Message);
            }

            existing ??= new List<WledData>();

            var mDns = new MulticastService();
            var sd = new ServiceDiscovery(mDns);
            mDns.NetworkInterfaceDiscovered += (s, e) => {
                // Ask for the name of all services.
                sd.QueryServiceInstances("_wled._tcp");
            };

            sd.ServiceDiscovered += (s, serviceName) => {
                mDns.SendQuery(serviceName, type: DnsType.PTR);
            };

            sd.ServiceInstanceDiscovered += (s, e) => {
                var name = e.ServiceInstanceName.ToString();
                
                
				if (!name.Contains("wled", StringComparison.InvariantCulture)) return;
                var rr = e.Message.AdditionalRecords;
                
                foreach (var id in from msg in rr where msg.Type == DnsType.TXT select msg.CanonicalName.Split(".")[0]) {
                    var nData = new WledData(id);
                    foreach (var ee in existing.Where(ee => ee.Id == nData.Id)) {
                        nData.CopyExisting(ee);
                    }
                    DataUtil.InsertCollection<WledData>("Dev_Wled", nData);
                    _discovered.Add(nData);
                }
            };

            mDns.Start();
            Log.Debug($"WLED: Discovery Started, timeout is {timeout}.");
            await Task.Delay(timeout * 1000);
            mDns.Stop();
            sd.Dispose();
            mDns.Dispose();
            Log.Debug($"WLED: Discovery complete, found {_discovered.Count} devices.");
            return _discovered;
        }
    }
}