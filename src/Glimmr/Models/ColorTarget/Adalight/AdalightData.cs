﻿#region

using System;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;

#endregion

namespace Glimmr.Models.ColorTarget.Adalight {
	[Serializable]
	public class AdalightData : IColorTargetData {
		[DefaultValue(false)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool ReverseStrip { get; set; }

		[DefaultValue(100)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int Brightness { get; set; }

		[DefaultValue(0)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int LedCount { get; set; }

		[JsonProperty] public float LedMultiplier { get; set; } = 1.0f;

		[DefaultValue(0)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int Offset { get; set; }

		[DefaultValue(115200)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int Speed { get; set; }

		[DefaultValue("COM1")]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public string Port { get; set; }

		[DefaultValue(2.2)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public float GammaFactor { get; set; } = 2.2f;
		[JsonProperty] public string Tag { get; set; } = "Adalight";


		public AdalightData() {
			Port = "COM1";
			Name = $"Adalight - {Port}";
			Id = Name;
			Brightness = 100;
			LedCount = 0;
			Speed = 115200;
			IpAddress = "localhost";
			LastSeen = DateTime.Now.ToString(CultureInfo.InvariantCulture);
		}

		public AdalightData(string port, int ledCount) {
			Port = port;
			Name = $"Adalight - {port}";
			Id = Name;
			Brightness = 100;
			LedCount = ledCount;
			Speed = 115200;
			IpAddress = "localhost";
			LastSeen = DateTime.Now.ToString(CultureInfo.InvariantCulture);
		}

		[JsonProperty] public string Id { get; set; }

		[JsonProperty] public string IpAddress { get; set; }

		[JsonProperty] public string LastSeen { get; set; }

		[JsonProperty] public string Name { get; set; }

		[DefaultValue(false)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool Enable { get; set; }

		public SettingsProperty[] KeyProperties { get; set; } = {
			new("ledmap", "ledmap", ""),
			new("Offset", "text", "Led Offset"),
			new("LedCount", "text", "Led Count"),
			new("LedMultiplier", "ledMultiplier", ""),
			new("GammaFactor", "number", "Gamma Correction")
				{ValueMin = "1.0",ValueMax = "5", ValueStep = ".1", ValueHint = "1 = No adjustment, 2.2 = Recommended"},
			new("Speed", "text", "Connection Speed (Baud Rate)"),
			new("ReverseStrip", "check", "Reverse Strip")
				{ValueHint = "Reverse the order of the leds to clockwise (facing screen)."}
		};


		public void UpdateFromDiscovered(IColorTargetData data) {
		}
	}
}