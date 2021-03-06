﻿using System;
using System.Collections.Generic;
using System.Linq;
using Multitouch.Framework.Input.Service;

namespace Multitouch.Framework.Input
{
	/// <summary>
	/// Frame event arguments
	/// </summary>
	public class FrameEventArgs : EventArgs
	{
		readonly FrameData data;
		private readonly IntPtr relativeTo;

		internal FrameEventArgs(FrameData data, IntPtr relativeTo)
		{
			this.data = data;
			this.relativeTo = relativeTo;
		}

		/// <summary>
		/// All contacts in this frame.
		/// </summary>
		public IEnumerable<Contact> Contacts
		{
			get { return data.Contacts.Select(c => new Contact(c, relativeTo, data.Timestamp)); }
		}

		/// <summary>
		/// Tries the get camera image.
		/// </summary>
		/// <param name="imageType">Type of the image.</param>
		/// <param name="left">The left.</param>
		/// <param name="top">The top.</param>
		/// <param name="width">The width.</param>
		/// <param name="height">The height.</param>
		/// <param name="imageData">The image data.</param>
		/// <returns></returns>
		public bool TryGetImage(ImageType imageType, int left, int top, int width, int height, out ImageData imageData)
		{
			bool result = false;
			imageData = null;

			Service.ImageType type = GetImageType(imageType);

			Service.ImageData image = data.Images.FirstOrDefault(i => i.Type == type);
			if(image != null)
			{
				int startAddress = top * image.Stride + left;
				int endAddress = (top + height - 1) * image.Stride + (left + width);
				if (endAddress <= image.Data.Length)
				{
					byte[] pixels = new byte[endAddress - startAddress];
					Array.Copy(image.Data, startAddress, pixels, 0, pixels.Length);
					imageData = new ImageData(width, height, image.Stride, image.BitsPerPixel, pixels);
					result = true;
				}
			}
			return result;
		}

		static Service.ImageType GetImageType(ImageType imageType)
		{
			switch (imageType)
			{
				case ImageType.Normalized:
					return Service.ImageType.Normalized;
				case ImageType.Binarized:
					return Service.ImageType.Binarized;
				default:
					throw new ArgumentOutOfRangeException("imageType");
			}
		}
	}
}
