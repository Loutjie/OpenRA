#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class WRotTest
	{
		// Terrain ramp orientations are tilted rotations whose quaternions round to slightly less than unit length.
		// Interpolating between two equal or nearly equal ones must not divide by zero (Move.MovePart crashed here).
		[TestCase(-64, 0, -64, 0)]
		[TestCase(-40, 0, -32, 0)]
		[TestCase(-32, -32, -32, -28)]
		public void SLerp_BetweenNearlyEqualTilts_DoesNotThrow(int roll1, int pitch1, int roll2, int pitch2)
		{
			var a = new WRot(new WAngle(roll1), new WAngle(pitch1), WAngle.Zero);
			var b = new WRot(new WAngle(roll2), new WAngle(pitch2), WAngle.Zero);

			Assert.DoesNotThrow(() => WRot.SLerp(a, b, 1, 4));
		}

		[Test]
		public void SLerp_OfATiltWithItself_ReturnsIt()
		{
			var tilt = new WRot(new WAngle(-64), WAngle.Zero, WAngle.Zero);
			Assert.That(WRot.SLerp(tilt, tilt, 1, 4), Is.EqualTo(tilt));
		}
	}
}
