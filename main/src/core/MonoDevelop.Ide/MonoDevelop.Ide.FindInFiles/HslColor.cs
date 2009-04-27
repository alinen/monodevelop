// 
// Hsl.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using Gdk;

namespace MonoDevelop.Ide.FindInFiles
{
	public struct HslColor
	{
		public double H {
			get;
			set;
		}
		
		public double S {
			get;
			set;
		}
		
		public double L {
			get;
			set;
		}
		
		static Gdk.Color black = new  Gdk.Color (0, 0, 0);
		public static implicit operator Color (HslColor hsl)
		{
			double r = 0, g = 0, b = 0;
			
			if (hsl.L == 0)
				return black;
			
			if (hsl.S == 0) {
				r = g = b = hsl.L;
			} else {
				double temp2 = hsl.L <= 0.5 ? hsl.L * (1.0 + hsl.S) : hsl.L + hsl.S -(hsl.L * hsl.S);
				double temp1 = 2.0 * hsl.L - temp2;
				
				double[] t3 = new double[] { hsl.H + 1.0 / 3.0, hsl.H, hsl.H - 1.0 / 3.0};
				double[] clr= new double[] { 0, 0, 0};
				for (int i = 0; i < 3; i++) {
					if (t3[i] < 0)
						t3[i] += 1.0;
					if (t3[i] > 1)
						t3[i]-=1.0;
					if (6.0 * t3[i] < 1.0)
						clr[i] = temp1 + (temp2 - temp1) * t3[i] * 6.0;
					else if (2.0 * t3[i] < 1.0)
						clr[i] = temp2;
					else if (3.0 * t3[i] < 2.0)
						clr[i] = (temp1 + (temp2 - temp1) * ((2.0 / 3.0) - t3[i]) * 6.0);
					else
						clr[i] = temp1;
				}
				
				r = clr[0];
				g = clr[1];
				b = clr[2];
			}
			return new Color ((byte)(255 * r), 
			                  (byte)(255 * g), 
			                  (byte)(255 * b));
		}
		
		public static implicit operator HslColor (Color color)
		{
			return new HslColor (color);
		}
		
		public HslColor (Color color)
		{
			double r = color.Red   / (double)ushort.MaxValue;
			double g = color.Green / (double)ushort.MaxValue;
			double b = color.Blue  / (double)ushort.MaxValue;
			
			H = S = L = 0;

			double v = Math.Max (r, g);
			v = Math.Max (v, b);

			double m = Math.Min (r, g);
			m = Math.Min (m, b);
			
			this.L = (m + v) / 2.0;
			if (this.L <= 0.0)
				return;
			double vm = v - m;
			this.S = vm;
			
			if (this.S > 0.0) {
				this.S /= (this.L <= 0.5) ? (v + m) : (2.0 - v - m);
			} else {
				return;
			}
			
			double r2 = (v - r) / vm;
			double g2 = (v - g) / vm;
			double b2 = (v - b) / vm;
			
			if (r == v) {
				this.H = (g == m ? 5.0 + b2 : 1.0 - g2);
			} else if (g == v) {
				this.H = (b == m ? 1.0 + r2 : 3.0 - b2);
			} else {
				this.H = (r == m ? 3.0 + g2 : 5.0 - r2);
			}
			this.H /= 6.0;
		}
		
		public override string ToString ()
		{
			return string.Format ("[HslColor: H={0}, S={1}, L={2}]", H, S, L);
		}
	}
}
