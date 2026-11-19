using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Audio;

internal static class AudioCoverage
{
    public static void MenuOnly(string root)
    {
        using var engine = new AudioEngine(Path.Combine(root, "PIXEL_Sounds.xgs"));
        using var waves = new WaveBank(engine, Path.Combine(root, "PIXEL_WaveBank.xwb"));
        using var sounds = new SoundBank(engine, Path.Combine(root, "PIXEL_SoundBank.xsb"));
        using var cue = sounds.GetCue("MUS_TITLE");
        Console.WriteLine("MENU_AUDIO_START cue=MUS_TITLE");
        cue.Play(); Pump(engine, 10000);
        if (!cue.IsPlaying) throw new InvalidOperationException("Title music stopped unexpectedly.");
        cue.Stop(AudioStopOptions.Immediate);
        Console.WriteLine("MENU_AUDIO_EXIT cue=MUS_TITLE");
    }

    public static unsafe void Run(string root)
    {
        using var engine = new AudioEngine(Path.Combine(root, "PIXEL_Sounds.xgs"));
        using var waves = new WaveBank(engine, Path.Combine(root, "PIXEL_WaveBank.xwb"));
        using var sounds = new SoundBank(engine, Path.Combine(root, "PIXEL_SoundBank.xsb"));
        var handle = (IntPtr)typeof(SoundBank).GetField("handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sounds)!;
        Check(GetNumCues(handle, out var count));
        for (ushort index = 0; index < count; index++)
        {
            Check(GetCueProperties(handle, index, out var properties));
            var name = Marshal.PtrToStringAnsi((IntPtr)properties.Name) ?? throw new InvalidDataException("Unnamed cue.");
            using var cue = sounds.GetCue(name);
            cue.Play();
            Pump(engine, 80);
            Console.WriteLine($"AUDIO_CUE_OK index={index} name={name} playing={cue.IsPlaying} stopped={cue.IsStopped}");
            cue.Stop(AudioStopOptions.Immediate);
        }
        foreach (var name in new[] { "MUS_TITLE", "MUS_GAME_Tuto_Prairie" })
        {
            using var cue = sounds.GetCue(name);
            cue.Play(); Pump(engine, 300);
            if (!cue.IsPlaying) throw new InvalidOperationException($"Music did not start: {name}");
            cue.Pause(); Pump(engine, 100);
            if (!cue.IsPaused) throw new InvalidOperationException($"Music did not pause: {name}");
            cue.Resume(); Pump(engine, 100);
            if (cue.IsPaused || !cue.IsPlaying) throw new InvalidOperationException($"Music did not resume: {name}");
            var category = engine.GetCategory("Music");
            category.Pause(); Pump(engine, 100); category.Resume();
            foreach (float volume in new[] { 0f, 0.25f, 1f })
            {
                category.SetVolume(volume); Pump(engine, 400);
                Console.WriteLine($"AUDIO_VOLUME_OK cue={name} category=Music volume={volume}");
            }
            cue.Stop(AudioStopOptions.Immediate); Pump(engine, 30);
            if (!cue.IsStopped) throw new InvalidOperationException($"Music did not stop: {name}");
            Console.WriteLine($"AUDIO_TRANSPORT_OK cue={name} pause=ok resume=ok stop=ok");
        }
        if (count == 0 || !waves.IsPrepared) throw new InvalidDataException("Audio bank was not prepared.");
        Console.WriteLine($"AUDIO_COVERAGE_OK cues={count} renderer={engine.RendererDetails[0].FriendlyName}");
    }

    private static void Pump(AudioEngine engine, int milliseconds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { engine.Update(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < milliseconds);
    }

    private static void Check(uint result)
    {
        if (result != 0) throw new InvalidOperationException($"FACT diagnostic call failed: 0x{result:X8}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CueProperties
    {
        public fixed byte Name[255];
        public int Interactive;
        public ushort Variable, Variations;
        public byte MaxInstances, CurrentInstances;
    }

    [DllImport("FAudio.dll", EntryPoint = "FACTSoundBank_GetNumCues", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetNumCues(IntPtr bank, out ushort count);
    [DllImport("FAudio.dll", EntryPoint = "FACTSoundBank_GetCueProperties", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetCueProperties(IntPtr bank, ushort index, out CueProperties properties);
}
