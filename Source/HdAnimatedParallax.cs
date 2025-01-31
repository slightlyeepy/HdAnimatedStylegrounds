// This file uses code from Maddie's Helping Hand:
//
// The MIT License (MIT)
//
// Copyright (c) 2019 maddie480
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Celeste.Mod.HdAnimatedStylegrounds {
	public class HdAnimatedParallax : Parallax {
		public static void Load() {
			IL.Celeste.MapData.ParseBackdrop += onParseBackdrop;
			IL.Celeste.Level.Render += onLevelRender;
			IL.Celeste.Parallax.Render += onParallaxRender;
		}

		public static void Unload() {
			IL.Celeste.MapData.ParseBackdrop -= onParseBackdrop;
			IL.Celeste.Level.Render -= onLevelRender;
			IL.Celeste.Parallax.Render -= onParallaxRender;
		}

		private static void onParseBackdrop(ILContext il) {
			ILCursor cursor = new ILCursor(il);
			while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchNewobj(typeof(Parallax)))) {
				Logger.Log("HdAnimatedStylegrounds/HdAnimatedParallax", $"Handling HD animated parallaxes at {cursor.Index} in IL for MapData.ParseBackdrop");

				cursor.EmitDelegate<Func<Parallax, Parallax>>(orig => {
					if (orig.Texture?.AtlasPath?.StartsWith("bgs/HdAnimatedStylegrounds/hdAnimatedParallax/") ?? false) {
						return new HdAnimatedParallax(orig.Texture);
					}
					return orig;
				});
			}
		}

		private class ParallaxMeta {
			public float? FPS { get; set; } = null;
			public string Frames { get; set; } = null;
		}


		private readonly List<MTexture> frames;
		private readonly int[] frameOrder;
		private readonly float fps;

		private int currentFrame;
		private float currentFrameTimer;

		public HdAnimatedParallax(MTexture texture) : base(texture) {
			// remove the frame number, much like decals do.
			string texturePath = Regex.Replace(texture.AtlasPath, "\\d+$", string.Empty);

			// then load all frames from that prefix.
			frames = GFX.Game.GetAtlasSubtextures(texturePath);

			// by default, the frames are just in order and last the same duration.
			frameOrder = new int[frames.Count];
			for (int i = 0; i < frameOrder.Length; i++) {
				frameOrder[i] = i;
			}

			Match fpsCount = Regex.Match(texturePath, "[^0-9]((?:[0-9]+\\.)?[0-9]+)fps$");
			if (fpsCount.Success) {
				// we found an FPS count! use it.
				fps = float.Parse(fpsCount.Groups[1].Value);
			} else {
				// use 12 FPS by default, like decals.
				fps = 12f;
			}

			if (Everest.Content.Map.TryGetValue("Graphics/Atlases/Gameplay/" + texturePath + ".meta", out ModAsset metaYaml) && metaYaml.Type == typeof(AssetTypeYaml)) {
				// the styleground has a metadata file! we should read it.
				ParallaxMeta meta;
				using (TextReader r = new StreamReader(metaYaml.Stream)) {
					meta = YamlHelper.Deserializer.Deserialize<ParallaxMeta>(r);
				}

				if (meta.FPS != null) {
					fps = meta.FPS.Value;
				}

				if (meta.Frames != null) {
					frameOrder = Calc.ReadCSVIntWithTricks(meta.Frames);
				}
			}

			Texture = frames[frameOrder[0]];
			currentFrame = 0;
			currentFrameTimer = 1f / fps;
		}

		public override void Update(Scene scene) {
			base.Update(scene);

			if (IsVisible(scene as Level)) {
				currentFrameTimer -= Engine.DeltaTime;
				if (currentFrameTimer < 0f) {
					currentFrameTimer += (1f / fps);
					currentFrame++;
					currentFrame %= frameOrder.Length;
					Texture = frames[frameOrder[currentFrame]];
				}
			}
		}

		public override void Render(Scene scene) {
			// don't render the usual way!
		}

		private static void onLevelRender(ILContext il) {
			ILCursor cursor = new ILCursor(il);

			if (cursor.TryGotoNext(instr => instr.MatchLdnull(), instr => instr.MatchCallvirt<GraphicsDevice>("SetRenderTarget"))
				&& cursor.TryGotoNext(instr => instr.MatchCallvirt<SpriteBatch>("Begin"))) {

				Logger.Log("HdAnimatedStylegrounds/HdAnimatedParallax", $"Inserting HD animated BG parallax rendering at {cursor.Index} in IL for Level.Render");
				cursor.EmitDelegate<Action>(() => renderHdAnimatedParallaxes(fg: false));

				if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<SpriteBatch>("End"))) {
					Logger.Log("HdAnimatedStylegrounds/HdAnimatedParallax", $"Inserting HD animated FG parallax rendering at {cursor.Index} in IL for Level.Render");
					cursor.EmitDelegate<Action>(() => renderHdAnimatedParallaxes(fg: true));
				}
			}
		}

		private static void renderHdAnimatedParallaxes(bool fg) {
			if (Engine.Scene is Level level) {
				foreach (Backdrop backdrop in (fg ? level.Foreground.Backdrops : level.Background.Backdrops)) {
					if (backdrop is HdAnimatedParallax hdAnimatedParallax) {
						level.BackgroundColor = Color.Transparent;
						hdAnimatedParallax.renderForReal(level);
					}
				}
			}
		}

		private void renderForReal(Scene scene) {
			Matrix matrix = Engine.ScreenMatrix;
			if (SaveData.Instance.Assists.MirrorMode) {
				matrix *= Matrix.CreateTranslation(-Engine.Viewport.Width, 0f, 0f);
				matrix *= Matrix.CreateScale(-1f, 1f, 1f);
			}

			Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState, SamplerState.PointWrap, DepthStencilState.None, RasterizerState.CullNone, ColorGrade.Effect, matrix);
			base.Render(scene);
			Draw.SpriteBatch.End();
		}

		private static void onParallaxRender(ILContext il) {
			ILCursor cursor = new ILCursor(il);

			float[] lookingFor = { 90f, 160f, 320f, 180f };
			while (cursor.TryGotoNext(MoveType.After, instr => instr.OpCode == OpCodes.Ldc_R4 && lookingFor.Contains((float) instr.Operand))) {
				Logger.Log("HdAnimatedStylegrounds/HdAnimatedParallax", $"Replacing parallax resolution at {cursor.Index} in IL for Parallax.Render");

				cursor.Emit(OpCodes.Ldarg_0);
				cursor.EmitDelegate<Func<float, Parallax, float>>((orig, self) => {
					if (self is HdAnimatedParallax) {
						return orig * 6; // 1920x1080 is 6 times 320x180.
					}
					return orig;
				});
			}
		}
	}
}
