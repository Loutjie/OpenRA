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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Primitives;

namespace OpenRA.Mods.Cnc.Graphics
{
	public class ModelRenderable : IPalettedRenderable, IModifyableRenderable
	{
		readonly ModelRenderer renderer;
		readonly IEnumerable<ModelAnimation> models;
		readonly WRot camera;
		readonly WRot lightSource;
		readonly ImmutableArray<float> lightAmbientColor;
		readonly ImmutableArray<float> lightDiffuseColor;
		readonly PaletteReference normalsPalette;
		readonly PaletteReference shadowPalette;
		readonly float scale;
		readonly WDist depthOffset;

		public ModelRenderable(
			ModelRenderer renderer, IEnumerable<ModelAnimation> models, WPos pos, int zOffset, in WRot camera, float scale,
			in WRot lightSource, ImmutableArray<float> lightAmbientColor, ImmutableArray<float> lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadow)
			: this(renderer, models, pos, zOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				color, normals, shadow, 1f,
				Vector3.One, TintModifiers.None)
		{ }

		public ModelRenderable(
			ModelRenderer renderer, IEnumerable<ModelAnimation> models, WPos pos, int zOffset, in WRot camera, float scale,
			in WRot lightSource, ImmutableArray<float> lightAmbientColor, ImmutableArray<float> lightDiffuseColor,
			PaletteReference color, PaletteReference normals, PaletteReference shadow,
			float alpha, in Vector3 tint, TintModifiers tintModifiers, WDist depthOffset = default)
		{
			this.renderer = renderer;
			this.models = models;
			Pos = pos;
			ZOffset = zOffset;
			this.scale = scale;
			this.camera = camera;
			this.lightSource = lightSource;
			this.lightAmbientColor = lightAmbientColor;
			this.lightDiffuseColor = lightDiffuseColor;
			Palette = color;
			normalsPalette = normals;
			shadowPalette = shadow;
			Alpha = alpha;
			Tint = tint;
			TintModifiers = tintModifiers;
			this.depthOffset = depthOffset;
		}

		public WPos Pos { get; }
		public PaletteReference Palette { get; }
		public int ZOffset { get; }
		public bool IsDecoration => false;

		public float Alpha { get; }
		public Vector3 Tint { get; }
		public TintModifiers TintModifiers { get; }

		public IPalettedRenderable WithPalette(PaletteReference newPalette)
		{
			return new ModelRenderable(
				renderer, models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				newPalette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers, depthOffset);
		}

		public IRenderable WithZOffset(int newOffset)
		{
			return new ModelRenderable(
				renderer, models, Pos, newOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers, depthOffset);
		}

		public IRenderable OffsetBy(in WVec vec)
		{
			return new ModelRenderable(
				renderer, models, Pos + vec, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers, depthOffset);
		}

		public IRenderable AsDecoration() { return this; }

		/// <summary>
		/// Draws the model this much nearer the camera without moving it on screen or moving its shadow,
		/// e.g. to keep a flying actor in front of the buildings it passes over.
		/// </summary>
		public ModelRenderable WithDepthOffset(WDist offset)
		{
			return new ModelRenderable(
				renderer, models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, Tint, TintModifiers, offset);
		}

		public IModifyableRenderable WithAlpha(float newAlpha)
		{
			return new ModelRenderable(
				renderer, models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, newAlpha, Tint, TintModifiers, depthOffset);
		}

		public IModifyableRenderable WithTint(in Vector3 newTint, TintModifiers newTintModifiers)
		{
			return new ModelRenderable(
				renderer, models, Pos, ZOffset, camera, scale,
				lightSource, lightAmbientColor, lightDiffuseColor,
				Palette, normalsPalette, shadowPalette, Alpha, newTint, newTintModifiers, depthOffset);
		}

		public IFinalizedRenderable PrepareRender(WorldRenderer wr)
		{
			return new FinalizedModelRenderable(wr, this);
		}

		sealed class FinalizedModelRenderable : IFinalizedRenderable
		{
			readonly ModelRenderable model;
			readonly ModelRenderProxy renderProxy;

			public FinalizedModelRenderable(WorldRenderer wr, ModelRenderable model)
			{
				this.model = model;
				var draw = model.models.Where(v => v.IsVisible);

				var map = wr.World.Map;

				// Flying actors cast a flat shadow, so it does not snap to each ramp they pass over.
				var groundOrientation = map.DistanceAboveTerrain(model.Pos).Length >= ShadowGround.FlatShadowAltitude
					? WRot.None : map.TerrainOrientation(map.CellContaining(model.Pos));
				renderProxy = model.renderer.RenderAsync(
					wr, draw, model.camera, model.scale, groundOrientation, model.lightSource,
					model.lightAmbientColor, model.lightDiffuseColor,
					model.Palette, model.normalsPalette, model.shadowPalette);
			}

			public void Render(WorldRenderer wr)
			{
				var map = wr.World.Map;
				var groundPos = new WPos(model.Pos.X, model.Pos.Y, ShadowGround.Height(map, model.Pos, renderProxy.ShadowDirection));
				var groundZ = (float)map.Rules.TerrainInfo.TileSize.Height * (groundPos.Z - model.Pos.Z) / map.Grid.TileScale;
				var pxOrigin = wr.Screen3DPosition(model.Pos);

				// HACK: We don't have enough texture channels to pass the depth data to the shader
				// so for now just offset everything forward so that the back corner is rendered at pos.
				pxOrigin -= new Vector3(0, 0, Screen3DBounds(wr).Z.X);

				// HACK: The previous hack isn't sufficient for the ramp type that is half flat and half
				// sloped towards the camera. Offset it by another half cell to avoid clipping.
				var cell = map.CellContaining(model.Pos);
				if (map.Ramp.Contains(cell) && map.Ramp[cell] == 7)
					pxOrigin += new Vector3(0, 0, 0.5f * map.Rules.TerrainInfo.TileSize.Height);

				var shadowOrigin = pxOrigin - groundZ * new Vector3(renderProxy.ShadowDirection, 1, 0);

				var psb = renderProxy.ProjectedShadowBounds;
				var sa = shadowOrigin + psb[0];
				var sb = shadowOrigin + psb[2];
				var sc = shadowOrigin + psb[1];
				var sd = shadowOrigin + psb[3];

				var wrsr = Game.Renderer.WorldRgbaSpriteRenderer;
				var t = model.Tint;
				if (wr.TerrainLighting != null && (model.TintModifiers & TintModifiers.IgnoreWorldTint) == 0)
					t *= wr.TerrainLighting.TintAt(model.Pos);

				// Shader interprets negative alpha as a flag to use the tint colour directly instead of multiplying the sprite colour
				var a = model.Alpha;
				if ((model.TintModifiers & TintModifiers.ReplaceColor) != 0)
					a *= -1;

				wrsr.DrawSprite(renderProxy.ShadowSprite, sa, sb, sc, sd, t, a);

				// Depth follows world Y, so moving towards the camera only changes the sprite's depth.
				var depth = (float)map.Rules.TerrainInfo.TileSize.Height * model.depthOffset.Length / map.Grid.TileScale;
				wrsr.DrawSprite(renderProxy.Sprite, pxOrigin + new Vector3(0, 0, depth) - 0.5f * renderProxy.Sprite.Size, 1f, t, a);
			}

			public void RenderDebugGeometry(WorldRenderer wr)
			{
				var groundPos = new WPos(model.Pos.X, model.Pos.Y, ShadowGround.Height(wr.World.Map, model.Pos, renderProxy.ShadowDirection));
				var groundZ = wr.World.Map.Rules.TerrainInfo.TileSize.Height * (groundPos.Z - model.Pos.Z) / 1024f;
				var pxOrigin = wr.Screen3DPosition(model.Pos);
				var shadowOrigin = pxOrigin - groundZ * new Vector3(renderProxy.ShadowDirection, 1, 0);

				// Draw sprite rect
				var offset = pxOrigin + renderProxy.Sprite.Offset - 0.5f * renderProxy.Sprite.Size;
				var tl = wr.Viewport.WorldToViewPx(int2.FromVector(offset));
				var br = wr.Viewport.WorldToViewPx(int2.FromVector(offset + renderProxy.Sprite.Size));
				Game.Renderer.RgbaColorRenderer.DrawRect(tl.ToVector3(), br.ToVector3(), 1, Color.Red);

				// Draw transformed shadow sprite rect
				var c = Color.Purple;
				var psb = renderProxy.ProjectedShadowBounds;

				Game.Renderer.RgbaColorRenderer.DrawPolygon(
				[
					wr.Viewport.WorldToViewPx(shadowOrigin + psb[1]).ToVector3(),
					wr.Viewport.WorldToViewPx(shadowOrigin + psb[3]).ToVector3(),
					wr.Viewport.WorldToViewPx(shadowOrigin + psb[0]).ToVector3(),
					wr.Viewport.WorldToViewPx(shadowOrigin + psb[2]).ToVector3()
				], 1, c);

				// Draw bounding box
				var draw = model.models.Where(v => v.IsVisible);
				var scaleTransform = Util.ScaleMatrix(model.scale, model.scale, model.scale);
				var cameraTransform = Util.MakeFloatMatrix(model.camera.AsMatrix());

				foreach (var v in draw)
				{
					var bounds = v.Model.Bounds(v.FrameFunc());
					var rotation = Util.MakeFloatMatrix(v.RotationFunc().AsMatrix());
					var worldTransform = Util.MatrixMultiply(scaleTransform, rotation);

					var pxPos = pxOrigin + wr.ScreenVectorComponents(v.OffsetFunc());
					var screenTransform = Util.MatrixMultiply(cameraTransform, worldTransform);
					DrawBoundsBox(wr, pxPos, screenTransform, bounds, 1, Color.Yellow);
				}
			}

			static readonly uint[] CornerXIndex = [0, 0, 0, 0, 3, 3, 3, 3];
			static readonly uint[] CornerYIndex = [1, 1, 4, 4, 1, 1, 4, 4];
			static readonly uint[] CornerZIndex = [2, 5, 2, 5, 2, 5, 2, 5];
			static void DrawBoundsBox(WorldRenderer wr, in Vector3 pxPos, float[] transform, float[] bounds, float width, Color c)
			{
				var cr = Game.Renderer.RgbaColorRenderer;
				var corners = new Vector2[8];
				for (var i = 0; i < 8; i++)
				{
					var vec = new[] { bounds[CornerXIndex[i]], bounds[CornerYIndex[i]], bounds[CornerZIndex[i]], 1 };
					var screen = Util.MatrixVectorMultiply(transform, vec);
					corners[i] = wr.Viewport.WorldToViewPx(pxPos + new Vector3(screen[0], screen[1], screen[2])).ToVector2();
				}

				// Front face
				cr.DrawPolygon([corners[0], corners[1], corners[3], corners[2]], width, c);

				// Back face
				cr.DrawPolygon([corners[4], corners[5], corners[7], corners[6]], width, c);

				// Horizontal edges
				cr.DrawLine(corners[0], corners[4], width, c);
				cr.DrawLine(corners[1], corners[5], width, c);
				cr.DrawLine(corners[2], corners[6], width, c);
				cr.DrawLine(corners[3], corners[7], width, c);
			}

			public Rectangle ScreenBounds(WorldRenderer wr)
			{
				return Screen3DBounds(wr).Bounds;
			}

			(Rectangle Bounds, Vector2 Z) Screen3DBounds(WorldRenderer wr)
			{
				var pxOrigin = wr.ScreenPosition(model.Pos).AsVector3();
				var draw = model.models.Where(v => v.IsVisible);
				var scaleTransform = Util.ScaleMatrix(model.scale, model.scale, model.scale);
				var cameraTransform = Util.MakeFloatMatrix(model.camera.AsMatrix());

				var minX = float.MaxValue;
				var minY = float.MaxValue;
				var minZ = float.MaxValue;
				var maxX = float.MinValue;
				var maxY = float.MinValue;
				var maxZ = float.MinValue;

				foreach (var v in draw)
				{
					var bounds = v.Model.Bounds(v.FrameFunc());
					var rotation = Util.MakeFloatMatrix(v.RotationFunc().AsMatrix());
					var worldTransform = Util.MatrixMultiply(scaleTransform, rotation);

					var pxPos = pxOrigin + wr.ScreenVectorComponents(v.OffsetFunc());
					var screenTransform = Util.MatrixMultiply(cameraTransform, worldTransform);

					for (var i = 0; i < 8; i++)
					{
						var vec = new float[] { bounds[CornerXIndex[i]], bounds[CornerYIndex[i]], bounds[CornerZIndex[i]], 1 };
						var screen = Util.MatrixVectorMultiply(screenTransform, vec);
						minX = Math.Min(minX, pxPos.X + screen[0]);
						minY = Math.Min(minY, pxPos.Y + screen[1]);
						minZ = Math.Min(minZ, pxPos.Z + screen[2]);
						maxX = Math.Max(maxX, pxPos.X + screen[0]);
						maxY = Math.Max(maxY, pxPos.Y + screen[1]);
						maxZ = Math.Max(minZ, pxPos.Z + screen[2]);
					}
				}

				return (Rectangle.FromLTRB((int)minX, (int)minY, (int)maxX, (int)maxY), new Vector2(minZ, maxZ));
			}
		}
	}

	/// <summary>
	/// The height a model's shadow is drawn at. The shadow falls along a ray from the model, so the
	/// height is taken where that ray meets the ground, not under the model. Near the ground the
	/// terrain is used as is. Higher up it blends into terrain interpolated between the corners of a
	/// fixed lattice, so a flying actor's shadow slides over cliffs and ramps instead of jumping.
	/// </summary>
	public static class ShadowGround
	{
		public const int FlatShadowAltitude = 512;
		public const int SmoothingAltitude = 1024;
		const int LatticeSpacing = 2048;
		const int RayStep = 64;
		const int MaxRaySteps = 512;
		const int BisectionSteps = 6;

		/// <summary>Shadow height for a model at <paramref name="pos"/> on <paramref name="map"/>.</summary>
		/// <param name="map">The map the model is on.</param>
		/// <param name="pos">Position of the model.</param>
		/// <param name="shadowDirection">Screen x offset of the shadow per screen pixel it drops.</param>
		public static int Height(Map map, WPos pos, float shadowDirection)
		{
			var tileSize = map.Rules.TerrainInfo.TileSize;
			var xPerDrop = shadowDirection * tileSize.Height / tileSize.Width;
			return Height(p => p.Z - map.DistanceAboveTerrain(p).Length, pos, xPerDrop);
		}

		/// <summary>Shadow height for a model at <paramref name="pos"/>, given the terrain height under any position.</summary>
		/// <param name="groundHeightAt">Terrain height directly under a position.</param>
		/// <param name="pos">Position of the model.</param>
		/// <param name="xPerDrop">World X the shadow moves for each world unit it drops.</param>
		public static int Height(Func<WPos, int> groundHeightAt, WPos pos, float xPerDrop)
		{
			var exact = groundHeightAt(pos);
			var altitude = pos.Z - exact;
			if (altitude <= 0)
				return exact;

			var w = Math.Min(1f, altitude / (float)SmoothingAltitude);

			// Ground height under the shadow ray after it has dropped by t.
			float Surface(float t)
			{
				var p = new WPos(pos.X + (int)Math.Round(xPerDrop * t), pos.Y, pos.Z);
				var e = groundHeightAt(p);
				return e + (Smoothed(groundHeightAt, p) - e) * w;
			}

			if (pos.Z <= Surface(0))
				return pos.Z;

			float above = 0;
			float below = RayStep;
			for (var i = 0; i < MaxRaySteps && pos.Z - below > Surface(below); i++)
			{
				above = below;
				below += RayStep;
			}

			for (var i = 0; i < BisectionSteps; i++)
			{
				var mid = (above + below) / 2;
				if (pos.Z - mid > Surface(mid))
					above = mid;
				else
					below = mid;
			}

			return (int)Math.Round(pos.Z - below);
		}

		static float Smoothed(Func<WPos, int> groundHeightAt, WPos pos)
		{
			var x0 = (int)Math.Floor(pos.X / (double)LatticeSpacing) * LatticeSpacing;
			var y0 = (int)Math.Floor(pos.Y / (double)LatticeSpacing) * LatticeSpacing;
			var fx = (pos.X - x0) / (float)LatticeSpacing;
			var fy = (pos.Y - y0) / (float)LatticeSpacing;

			float H(int dx, int dy) => groundHeightAt(new WPos(x0 + dx, y0 + dy, pos.Z));
			var north = H(0, 0) + (H(LatticeSpacing, 0) - H(0, 0)) * fx;
			var south = H(0, LatticeSpacing) + (H(LatticeSpacing, LatticeSpacing) - H(0, LatticeSpacing)) * fx;
			return north + (south - north) * fy;
		}
	}
}
