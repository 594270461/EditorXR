﻿#if UNITY_EDITOR

namespace UnityEditor.Experimental.EditorVR
{
	/// <summary>
	/// Gives decorated class ability to perform haptic feedback
	/// </summary>
	public interface IPerformHaptics
	{
	}

	public static class IPerformHapticsMethods
	{
		internal delegate void PerformHapticsDelegate(float duration, float intensity = 1f, bool fadeIn = false, bool fadeOut = false);

		internal static PerformHapticsDelegate performHaptics { get; set; }

		/// <summary>
		/// Method for performing haptic feedback
		/// </summary>
		/// <param name="duration">Duration of haptic feedback</param>
		/// <param name="intensity">Intensity of haptic feedback (optional)</param>
		public static void Pulse(this IPerformHaptics obj, float duration, float intensity = 1f, bool fadeIn = false, bool fadeOut = false)
		{
			performHaptics(duration, intensity, fadeIn, fadeOut);
		}
	}
}
#endif
