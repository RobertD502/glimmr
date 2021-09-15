﻿#region

using System;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;

#endregion

namespace Glimmr.Models.ColorTarget.Led {
	[Serializable]
	public class LedData : IColorTargetData {
		[DefaultValue(true)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool FixGamma { get; set; } = true;

		[DefaultValue(false)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool ReverseStrip { get; set; }


		[JsonProperty] public int Brightness { get; set; }

		[DefaultValue(18)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int GpioNumber { get; set; } = 18;

		[DefaultValue(300)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int LedCount { get; set; } = 300;

		[JsonProperty] public float LedMultiplier { get; set; } = 1.0f;

		[DefaultValue(30)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int MilliampsPerLed { get; set; } = 30;

		[DefaultValue(0)]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public int Offset { get; set; }

		[JsonProperty] public int StartupAnimation { get; set; }
		[JsonProperty] public int StripType { get; set; }

		[DefaultValue("Led")]
		[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
		public string Tag { get; set; } = "Led";

		public LedData() {
			LastSeen = DateTime.Now.ToString(CultureInfo.InvariantCulture);
		}

		[JsonProperty] public string Name => $"LED {Id} - GPIO {GpioNumber}";

		public string Id { get; set; } = "";

		[JsonProperty] public string IpAddress { get; set; } = "";

		[JsonProperty] public bool Enable { get; set; }

		[JsonProperty] public string LastSeen { get; set; }


		public void UpdateFromDiscovered(IColorTargetData data) {
		}

		[JsonProperty]
		public SettingsProperty[] KeyProperties { get; set; } = {
			new("ledmap", "ledmap", ""),
			new("LedCount", "text", "Led Count"),
			new("Offset", "text", "Led Offset"),
			new("LedMultiplier", "ledMultiplier", ""),
			new("ReverseStrip", "check", "Reverse Strip")
				{ValueHint = "Reverse the order of the leds to clockwise (facing screen)."},
			new("FixGamma", "check", "Fix Gamma") {ValueHint = "Automatically correct Gamma (recommended)"},
			new("MilliampsPerLed", "text", "Milliamps Per LED")
				{ValueHint = "'Default' = 30 (.3w), 'Normal' = 55 (.55w)"}
		};
	}
}