using System;

namespace Celeste.Mod.HdAnimatedStylegrounds;

public class HdAnimatedStylegroundsModule : EverestModule {
	public static HdAnimatedStylegroundsModule Instance { get; private set; }

	public HdAnimatedStylegroundsModule() {
		Instance = this;
#if DEBUG
		// debug builds use verbose logging
		Logger.SetLogLevel("HdAnimatedStylegrounds", LogLevel.Verbose);
#else
		// release builds use info logging to reduce spam in log files
		Logger.SetLogLevel("HdAnimatedStylegrounds", LogLevel.Info);
#endif
	}

	public override void Load() {
		HdAnimatedParallax.Load();
	}

	public override void Unload() {
		HdAnimatedParallax.Unload();
	}
}
