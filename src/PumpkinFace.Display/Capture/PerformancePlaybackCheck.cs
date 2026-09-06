using Godot;
using PumpkinFace.Core;
using PumpkinFace.Display.Animation;

namespace PumpkinFace.Display.Capture;

/// <summary>Engine-level audio/stop/retrigger check; never opens or changes saved operator settings.</summary>
public sealed partial class PerformancePlaybackCheck : Node
{
    private readonly CannedPerformancePlayer _player = new();
    private int _phase;
    private double _elapsed;
    private double _lastPosition;
    private bool _finished;
    private int _clipIndex;
    private readonly string[] _clips = [.. PerformanceLibrary.All.Select(c => c.Id), PerformanceLibrary.SongId];
    public override void _Ready()
    {
        AddChild(_player);
        _player.SetSoundEnabled(false);
        foreach (var clip in PerformanceLibrary.All)
        {
            Require(_player.Play(clip.Id), $"Could not load {clip.Title}.");
            _player.Stop();
            Require(!_player.Active, "Same-frame switching left an active scene.");
        }
        Require(!_player.LoadSong("/nonexistent/pumpkin-song.mp3", out _), "Missing audio file was accepted.");
        string songPath = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--verify-song="))?.Split('=', 2)[1]
            ?? ProjectSettings.GlobalizePath("res://Assets/Audio/boo.wav");
        Require(_player.LoadSong(songPath, out string error), error);
        Require(_player.Play(PerformanceLibrary.SongId), "Chosen song did not play.");
        Require(!_player.Play("missing") && _player.Current.Id == PerformanceLibrary.SongId && _player.Playing,
            "An unknown performance interrupted the current song.");
        Require(!_player.LoadSong("/nonexistent/pumpkin-song.mp3", out _) && _player.Playing,
            "An invalid song selection interrupted the current performance.");
        Require(_player.Play(), "Bundled sound could not load.");
        _player.Stop(); // A same-frame cancellation must not release an uninitialized pose.
        Require(!_player.Active, "Same-frame Stop left an active scene.");
        _player.Play("vader");
    }
    public override void _Process(double delta)
    {
        if (_finished) return;
        try
        {
            _elapsed += delta;
            if (_phase == 3)
            {
                // Allow the audio mixer to retire its stopped playback before engine shutdown.
                if (_elapsed > .25) { _finished = true; GetTree().Quit(); }
                return;
            }
            FacePose pose = _player.Sample(delta, FacePose.Neutral, .65f);
            Require(pose == pose.Clamp(), "Playback produced an invalid pose.");
            switch (_phase)
            {
                case 0 when _elapsed > 4.1:
                    Require(pose.SaberGlow > .5f, "Saber did not ignite during muted playback.");
                    Require(_player.Position > .2, "Muted audio clock did not advance.");
                    _lastPosition = _player.Position;
                    _player.SetSoundEnabled(true);
                    _player.SetSoundEnabled(false);
                    Require(_player.Position == _lastPosition, "Muting changed the timeline.");
                    _player.Stop();
                    Require(!_player.Playing, "Stop did not cancel playback.");
                    _phase = 1; _elapsed = 0;
                    break;
                case 1 when _elapsed > .4:
                    Require(!_player.Active && pose == FacePose.Neutral, "Stop did not settle to rest.");
                    _player.Play();
                    Require(_player.Position == 0, "Replay did not rewind.");
                    _phase = 2; _elapsed = 0;
                    break;
                case 2:
                    if (!_player.Playing)
                    {
                        Require(_player.Position == _player.Current.Duration, "Clip ended at the wrong time.");
                        Require(pose == FacePose.Neutral, "Completed scene did not return to rest.");
                        GD.Print($"PASS: {_player.Current.Title} completed at {_player.Position:0.00}s.");
                        if (++_clipIndex < _clips.Length)
                        {
                            Require(_player.Play(_clips[_clipIndex]), "Next clip could not play.");
                            Require(_player.Position == 0, "Switching clips did not rewind.");
                            _elapsed = 0;
                        }
                        else
                        {
                            GD.Print("PASS: switchboard audio, chosen song, invalid selection, muted clock, stop/release, replay, and completion.");
                            _player.QueueFree();
                            _phase = 3; _elapsed = 0;
                        }
                    }
                    else Require(_elapsed < _player.Current.Duration + 3, "Audio clock stalled before completion.");
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PushError(error.Message);
            _finished = true;
            GetTree().Quit(2);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
