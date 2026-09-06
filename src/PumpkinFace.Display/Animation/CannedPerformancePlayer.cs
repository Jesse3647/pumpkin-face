using Godot;
using PumpkinFace.Core;

namespace PumpkinFace.Display.Animation;

/// <summary>One audio clock drives the complete scene, even when its sound is muted.</summary>
public sealed partial class CannedPerformancePlayer : Node
{
    private AudioStreamPlayer? _audio;
    private readonly Dictionary<string, AudioStream> _tracks = [];
    private AudioStream? _song;
    private double _time;
    private double _release = .24;
    private FacePose _lastPose;
    private bool _audioFinished;
    private bool _hasSample;
    public double Position => _time;
    public bool Playing { get; private set; }
    public bool Active => Playing || _release < .24;
    public PerformanceDefinition Current { get; private set; } = PerformanceLibrary.Find("little-scare")!;
    public string BeatLabel => Current.Id == "little-scare" ? PlayfulScarePerformance.BeatLabel(_time) : Current.Title;
    public bool SoundEnabled { get; private set; } = true;
    public float SongTempo { get; set; } = 120;
    public string? SongTitle { get; private set; }

    public override void _Ready()
    {
        foreach (var clip in PerformanceLibrary.All.Where(c => c.AudioFile is not null))
        {
            AudioStream? stream = GD.Load<AudioStream>($"res://Assets/Audio/{clip.AudioFile}");
            if (stream is not null) _tracks[clip.Id] = stream;
        }
        _audio = new AudioStreamPlayer { Name = "PerformanceAudio", VolumeDb = -5 };
        AddChild(_audio);
        _audio.Finished += () => _audioFinished = true;
    }

    public override void _ExitTree()
    {
        _audio?.Stop();
        if (_audio is not null) _audio.Stream = null;
        foreach (var track in _tracks.Values) track.Dispose();
        _tracks.Clear();
        _song?.Dispose();
        _song = null;
    }

    public bool Play(string id = "little-scare")
    {
        PerformanceDefinition? definition = id == PerformanceLibrary.SongId && _song is not null
            ? new(id, SongTitle ?? "Your song", "Dance along", _song.GetLength(), null)
            : PerformanceLibrary.Find(id);
        if (definition is null) return false;
        Current = definition;
        _time = 0;
        _release = .24;
        _audioFinished = false;
        _hasSample = false;
        Playing = true;
        _audio?.Stop();
        AudioStream? track = id == PerformanceLibrary.SongId ? _song : _tracks.GetValueOrDefault(id);
        if (_audio is not null)
        {
            _audio.Stream = track;
            if (track is not null) _audio.Play();
        }
        return definition.AudioFile is null || track is not null;
    }

    public bool LoadSong(string path, out string error)
    {
        error = "Choose a playable MP3, WAV or Ogg audio file.";
        AudioStream? song = null;
        try
        {
            if (!System.IO.File.Exists(path)) return false;
            song = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => AudioStreamMP3.LoadFromFile(path),
                ".wav" => AudioStreamWav.LoadFromFile(path),
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                _ => null,
            };
            if (song is null || !double.IsFinite(song.GetLength()) || song.GetLength() <= 0)
            { song?.Dispose(); return false; }
            // An imported loop flag must never turn a one-shot selection into endless playback.
            if (song is AudioStreamMP3 mp3) mp3.Loop = false;
            if (song is AudioStreamOggVorbis ogg) ogg.Loop = false;
            if (song is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
            Stop();
            if (_audio is not null) _audio.Stream = null;
            _song?.Dispose();
            _song = song;
            SongTitle = System.IO.Path.GetFileNameWithoutExtension(path);
            error = "";
            return true;
        }
        catch (Exception)
        {
            song?.Dispose();
            return false;
        }
    }

    public void SetSoundEnabled(bool enabled)
    {
        SoundEnabled = enabled;
        // Muting must not stop playback or change the timing of the face.
        if (_audio is not null) _audio.VolumeDb = enabled ? -5 : -80;
    }

    public void Stop()
    {
        if (Playing && _hasSample) _release = 0;
        Playing = false;
        _audio?.Stop();
    }

    public FacePose Sample(double delta, FacePose expression, float motionAmount)
    {
        if (Playing)
        {
            if (_audioFinished) _time = Current.Duration;
            else if (_audio?.Playing == true)
                _time = Math.Max(_time, Math.Max(0, _audio.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency()));
            else _time += Math.Max(0, delta); // Keep the silent scene usable if an asset is unavailable.
            _time = Math.Min(_time, Current.Duration);
            _hasSample = true;
            _lastPose = PerformanceLibrary.Sample(Current.Id, _time, expression, motionAmount, Current.Duration, SongTempo);
            if (_time >= Current.Duration) { Playing = false; _audio?.Stop(); }
            return _lastPose;
        }
        if (_release < .24)
        {
            _release = Math.Min(.24, _release + Math.Max(0, delta));
            float t = (float)(_release / .24);
            return FacePose.Lerp(_lastPose, expression, t * t * (3 - 2 * t));
        }
        return expression;
    }
}
