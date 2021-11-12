﻿#region

using System;

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Glimmr.Models.Util;
using Serilog;

#endregion

namespace Glimmr.Models.ColorSource.Video.Stream.Screen {
	public class ScreenVideoStream : IVideoStream, IDisposable {
		private bool _capturing;
		private int _height;
		private int _left;
		private Rectangle _screenDims;
		private FrameSplitter? _splitter;
		private int _top;
		private int _width;
		private readonly OSxScreenshot? _ss;

		public ScreenVideoStream() {
			Log.Information("Config got.");
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				_ss = new OSxScreenshot();
			}
		}

		public void Dispose() {
			GC.SuppressFinalize(this);
		}

		public Task Start(FrameSplitter splitter, CancellationToken ct) {
			_splitter = splitter;
			try {
				SetDimensions();

				if (_width == 0 || _height == 0) {
					Log.Information("We have no screen, returning.");
					return Task.CompletedTask;
				}

				_capturing = true;
				return Task.Run(() => CaptureScreen(ct), CancellationToken.None);
			} catch (Exception e) {
				Log.Warning("Exception, can't start screen cap: " + e.Message);
				_capturing = false;
				return Task.CompletedTask;
			}
		}

		public Task Stop() {
			_capturing = false;
			Dispose();
			return Task.CompletedTask;
		}

		private void SetDimensions() {
			_screenDims = DisplayUtil.GetDisplaySize();
			var rect = _screenDims;
			_width = 0;
			_height = 0;
			_left = rect.Left;
			_top = rect.Top;
			_width = rect.Width;
			_height = rect.Height;
			_width = Math.Abs(_width);
			_height = Math.Abs(_height);
		}


		private void CaptureScreen(CancellationToken ct) {
			Log.Debug("Screen capture started...");
			while (!ct.IsCancellationRequested && _capturing) {
				try {
					Image<Bgr, byte>? newMat;
					if (_ss == null) {
						var bcs = new Bitmap(_width, _height, PixelFormat.Format24bppRgb);
						using var g = Graphics.FromImage(bcs);
						g.CopyFromScreen(_left, _top, 0, 0, bcs.Size, CopyPixelOperation.SourceCopy);
						g.Flush();
						var sc = bcs.ToImage<Bgr, byte>();
						newMat = sc.Resize(640, 480, Inter.Nearest);
					} else {
						newMat = _ss.Grab();
						if (newMat == null) return;
					}
					_splitter?.Update(newMat.Mat);
					newMat.Dispose();
				} catch (Exception e) {
					Log.Debug("Exception grabbing screen: " + e.Message);
				}
				
			}

			Log.Debug("Capture completed?");
		}
	}
}