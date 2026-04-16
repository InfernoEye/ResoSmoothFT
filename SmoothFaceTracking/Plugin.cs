using System.Runtime.CompilerServices;
using ResoniteModLoader;
using Renderite;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;

namespace SmoothFaceTracking;
public class SmoothFaceTracking : ResoniteMod
{
    internal const string VERSION_CONSTANT = "1.0.0"; // Version of the mod
    public override string Name => "SmoothFaceTracking";
    public override string Author => "ForgottenSin(InfernoEye)"; // Original by Purpzie
    public override string Version => VERSION_CONSTANT;
    public override string Link => "https://github.com/InfernoEye/ResoSmoothFT";

    [AutoRegisterConfigKey]
    private static ModConfigurationKey<bool> EYE_SMOOTHING_ENABLED = new("Enable Eye Smoothing", "Enables smoothing on eye tracking", () => true);
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<float> EYE_SMOOTHING_SPEED = new("Eye Smoothing Speed", "How fast to smooth eye tracking", () => 20f);
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<bool> MOUTH_SMOOTHING_ENABLED = new("Enable Mouth Smoothing", "Enables smoothing on mouth tracking", () => true);
    [AutoRegisterConfigKey]
    private static ModConfigurationKey<float> MOUTH_SMOOTHING_SPEED = new("Mouth Smoothing Speed", "How fast to smooth mouth tracking", () => 20f);

    private static readonly ConditionalWeakTable<EyeTrackingStreamManager.EyeStreams, EyeSmoothing> EYE_SMOOTHING_STORAGE = [];
    private static readonly ConditionalWeakTable<MouthTrackingStreamManager, MouthSmoothing> MOUTH_SMOOTHING_STORAGE = [];

    private static ModConfiguration? Config;
    public override void OnEngineInit()
	{
        Config = GetConfiguration();
        Config?.Save(true);
		Harmony harmony = new Harmony("InfernoEye.SmoothFaceTracking");
        harmony.PatchAll();
	}

	[HarmonyPatch(typeof(EyeTrackingStreamManager.EyeStreams))]
	private class PatchEyeStreams
	{
		// by the time this function runs, resonite has already ignored non-local users
		[HarmonyPostfix]
		[HarmonyPatch(nameof(EyeTrackingStreamManager.EyeStreams.UpdateFromEye))]
		private static void PostFix(EyeTrackingStreamManager.EyeStreams __instance)
		{
			if (!__instance.IsTracking.Target.Value || !EYE_SMOOTHING_ENABLED.Value)
				return;

			EYE_SMOOTHING_STORAGE.TryGetValue(__instance, out EyeSmoothing? eyeSmoothing);
			if (eyeSmoothing == null)
			{
				eyeSmoothing = new EyeSmoothing(__instance);
				EYE_SMOOTHING_STORAGE.Add(__instance, eyeSmoothing);
			}

			eyeSmoothing.ApplySmoothing(__instance, __instance.Time.Delta * EYE_SMOOTHING_SPEED.Value);
		}
	}

	[HarmonyPatch(typeof(MouthTrackingStreamManager))]
	private class PatchMouthTrackingStreamManager
	{
		[HarmonyPostfix]
		[HarmonyPatch("OnCommonUpdate")]
		private static void PostFix(MouthTrackingStreamManager __instance)
		{
			if (
				__instance.User.Target != __instance.LocalUser
				|| !__instance.IsTracking.Target.Value
				|| !MOUTH_SMOOTHING_ENABLED.Value
			)
				return;

			MOUTH_SMOOTHING_STORAGE.TryGetValue(__instance, out MouthSmoothing? mouthSmoothing);
			if (mouthSmoothing == null)
			{
				mouthSmoothing = new MouthSmoothing(__instance);
				MOUTH_SMOOTHING_STORAGE.Add(__instance, mouthSmoothing);
			}

			mouthSmoothing.ApplySmoothing(__instance, __instance.Time.Delta * MOUTH_SMOOTHING_SPEED.Value);
		}
	}

	private class FloatSmoother
	{
		private float Current;
		private float Intermediate;

		public FloatSmoother(SyncRef<ValueStream<float>> stream)
		{
			Current = stream.Target?.Value ?? 0f;
			Intermediate = Current;
		}

		public void Smooth(SyncRef<ValueStream<float>> stream, float delta)
		{
			var target = stream.Target;
			if (target != null)
			{
				Current = MathX.SmoothLerp(Current, target.Value, ref Intermediate, delta);
				target.Value = Current;
			}
		}
	}

	private class Float3Smoother
	{
		private float3 Current;
		private float3 Intermediate;

		public Float3Smoother(SyncRef<ValueStream<float3>> stream)
		{
			Current = stream.Target?.Value ?? float3.Zero;
			Intermediate = Current;
		}

		public void Smooth(SyncRef<ValueStream<float3>> stream, float delta)
		{
			var target = stream.Target;
			if (target != null)
			{
				Current = MathX.SmoothLerp(Current, target.Value, ref Intermediate, delta);
				target.Value = Current;
			}
		}
	}

	private class EyeSmoothing(EyeTrackingStreamManager.EyeStreams eyeStreams)
	{
		private readonly Float3Smoother Direction = new(eyeStreams.Direction);
		private readonly Float3Smoother Position = new(eyeStreams.Position);
		private readonly FloatSmoother Openness = new(eyeStreams.Openness);
		private readonly FloatSmoother Widen = new(eyeStreams.Widen);
		private readonly FloatSmoother Squeeze = new(eyeStreams.Squeeze);
		private readonly FloatSmoother Frown = new(eyeStreams.Frown);
		private readonly FloatSmoother InnerBrowVertical = new(eyeStreams.InnerBrowVertical);
		private readonly FloatSmoother OuterBrowVertical = new(eyeStreams.OuterBrowVertical);

		public void ApplySmoothing(EyeTrackingStreamManager.EyeStreams eyeStreams, float delta)
		{
			Direction.Smooth(eyeStreams.Direction, delta);
			Position.Smooth(eyeStreams.Position, delta);
			Openness.Smooth(eyeStreams.Openness, delta);
			Widen.Smooth(eyeStreams.Widen, delta);
			Squeeze.Smooth(eyeStreams.Squeeze, delta);
			Frown.Smooth(eyeStreams.Frown, delta);
			InnerBrowVertical.Smooth(eyeStreams.InnerBrowVertical, delta);
			OuterBrowVertical.Smooth(eyeStreams.OuterBrowVertical, delta);
		}
	}

	private class MouthSmoothing(MouthTrackingStreamManager mouth)
	{
		// please tell me there's a better way to do this
		private readonly Float3Smoother Jaw = new(mouth.Jaw);
		private readonly FloatSmoother JawOpen = new(mouth.JawOpen);
		private readonly Float3Smoother Tongue = new(mouth.Tongue);
		private readonly FloatSmoother TongueRoll = new(mouth.TongueRoll);
		private readonly FloatSmoother LipUpperLeftRaise = new(mouth.LipUpperLeftRaise);
		private readonly FloatSmoother LipUpperRightRaise = new(mouth.LipUpperRightRaise);
		private readonly FloatSmoother LipLowerLeftRaise = new(mouth.LipLowerLeftaise); // resonite is missing an R here
		private readonly FloatSmoother LipLowerRightRaise = new(mouth.LipLowerRightRaise);
		private readonly FloatSmoother LipUpperHorizontal = new(mouth.LipUpperHorizontal);
		private readonly FloatSmoother LipLowerHorizontal = new(mouth.LipLowerHorizontal);
		private readonly FloatSmoother MouthLeftSmileFrown = new(mouth.MouthLeftSmileFrown);
		private readonly FloatSmoother MouthRightSmileFrown = new(mouth.MouthRightSmileFrown);
		private readonly FloatSmoother MouthLeftDimple = new(mouth.MouthLeftDimple);
		private readonly FloatSmoother MouthRightDimple = new(mouth.MouthRightDimple);
		private readonly FloatSmoother MouthPoutLeft = new(mouth.MouthPoutLeft);
		private readonly FloatSmoother MouthPoutRight = new(mouth.MouthPoutRight);
		private readonly FloatSmoother LipTopLeftOverturn = new(mouth.LipTopLeftOverturn);
		private readonly FloatSmoother LipTopRightOverturn = new(mouth.LipTopRightOverturn);
		private readonly FloatSmoother LipBottomLeftOverturn = new(mouth.LipBottomLeftOverturn);
		private readonly FloatSmoother LipBottomRightOverturn = new(mouth.LipBottomRightOverturn);
		private readonly FloatSmoother LipTopLeftOverUnder = new(mouth.LipTopLeftOverUnder);
		private readonly FloatSmoother LipTopRightOverUnder = new(mouth.LipTopRightOverUnder);
		private readonly FloatSmoother LipBottomLeftOverUnder = new(mouth.LipBottomLeftOverUnder);
		private readonly FloatSmoother LipBottomRightOverUnder = new(mouth.LipBottomRightOverUnder);
		private readonly FloatSmoother LipLeftStretchTighten = new(mouth.LipLeftStretchTighten);
		private readonly FloatSmoother LipRightStretchTighten = new(mouth.LipRightStretchTighten);
		private readonly FloatSmoother LipsLeftPress = new(mouth.LipsLeftPress);
		private readonly FloatSmoother LipsRightPress = new(mouth.LipsRightPress);
		private readonly FloatSmoother CheekLeftPuffSuck = new(mouth.CheekLeftPuffSuck);
		private readonly FloatSmoother CheekRightPuffSuck = new(mouth.CheekRightPuffSuck);
		private readonly FloatSmoother CheekLeftRaise = new(mouth.CheekLeftRaise);
		private readonly FloatSmoother CheekRightRaise = new(mouth.CheekRightRaise);
		private readonly FloatSmoother NoseWrinkleLeft = new(mouth.NoseWrinkleLeft);
		private readonly FloatSmoother NoseWrinkleRight = new(mouth.NoseWrinkleRight);
		private readonly FloatSmoother ChinRaiseBottom = new(mouth.ChinRaiseBottom);
		private readonly FloatSmoother ChinRaiseTop = new(mouth.ChinRaiseTop);

		public void ApplySmoothing(MouthTrackingStreamManager mouth, float delta)
		{
			Jaw.Smooth(mouth.Jaw, delta);
			JawOpen.Smooth(mouth.JawOpen, delta);
			Tongue.Smooth(mouth.Tongue, delta);
			TongueRoll.Smooth(mouth.TongueRoll, delta);
			LipUpperLeftRaise.Smooth(mouth.LipUpperLeftRaise, delta);
			LipUpperRightRaise.Smooth(mouth.LipUpperRightRaise, delta);
			LipLowerLeftRaise.Smooth(mouth.LipLowerLeftaise, delta); // resonite is missing an R here
			LipLowerRightRaise.Smooth(mouth.LipLowerRightRaise, delta);
			LipUpperHorizontal.Smooth(mouth.LipUpperHorizontal, delta);
			LipLowerHorizontal.Smooth(mouth.LipLowerHorizontal, delta);
			MouthLeftSmileFrown.Smooth(mouth.MouthLeftSmileFrown, delta);
			MouthRightSmileFrown.Smooth(mouth.MouthRightSmileFrown, delta);
			MouthLeftDimple.Smooth(mouth.MouthLeftDimple, delta);
			MouthRightDimple.Smooth(mouth.MouthRightDimple, delta);
			MouthPoutLeft.Smooth(mouth.MouthPoutLeft, delta);
			MouthPoutRight.Smooth(mouth.MouthPoutRight, delta);
			LipTopLeftOverturn.Smooth(mouth.LipTopLeftOverturn, delta);
			LipTopRightOverturn.Smooth(mouth.LipTopRightOverturn, delta);
			LipBottomLeftOverturn.Smooth(mouth.LipBottomLeftOverturn, delta);
			LipBottomRightOverturn.Smooth(mouth.LipBottomRightOverturn, delta);
			LipTopLeftOverUnder.Smooth(mouth.LipTopLeftOverUnder, delta);
			LipTopRightOverUnder.Smooth(mouth.LipTopRightOverUnder, delta);
			LipBottomLeftOverUnder.Smooth(mouth.LipBottomLeftOverUnder, delta);
			LipBottomRightOverUnder.Smooth(mouth.LipBottomRightOverUnder, delta);
			LipLeftStretchTighten.Smooth(mouth.LipLeftStretchTighten, delta);
			LipRightStretchTighten.Smooth(mouth.LipRightStretchTighten, delta);
			LipsLeftPress.Smooth(mouth.LipsLeftPress, delta);
			LipsRightPress.Smooth(mouth.LipsRightPress, delta);
			CheekLeftPuffSuck.Smooth(mouth.CheekLeftPuffSuck, delta);
			CheekRightPuffSuck.Smooth(mouth.CheekRightPuffSuck, delta);
			CheekLeftRaise.Smooth(mouth.CheekLeftRaise, delta);
			CheekRightRaise.Smooth(mouth.CheekRightRaise, delta);
			NoseWrinkleLeft.Smooth(mouth.NoseWrinkleLeft, delta);
			NoseWrinkleRight.Smooth(mouth.NoseWrinkleRight, delta);
			ChinRaiseBottom.Smooth(mouth.ChinRaiseBottom, delta);
			ChinRaiseTop.Smooth(mouth.ChinRaiseTop, delta);
		}
	}
}
