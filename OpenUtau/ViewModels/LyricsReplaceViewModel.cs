﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class LyricsReplaceViewModel : ViewModelBase {
        [Reactive] public string OldValue { get; set; } = "";
        [Reactive] public string NewValue { get; set; } = "";
        [Reactive] public string Preview { get; set; } = "";
        public List<ReplacePreset> PresetList { get; } = new List<ReplacePreset>() { //Increase!
            new ReplacePreset("-", "", ""),
            new ReplacePreset(ThemeManager.GetString("lyricsreplace.preset.rmvalphabet"), @"[a-zA-Z]", ""),
            new ReplacePreset(ThemeManager.GetString("lyricsreplace.preset.rmvnonhiragana"), @"[^\p{IsHiragana}ヴ]+", ""),
            new ReplacePreset(ThemeManager.GetString("lyricsreplace.preset.rmvphonetichint"), @"\[.*\]", ""),
            new ReplacePreset(ThemeManager.GetString("lyricsreplace.preset.rmvtone"), @"_?[A-G](#|b)?[1-7]", ""),
            new ReplacePreset(ThemeManager.GetString("lyricsreplace.preset.rmvspace"), ".* ", "")
        };
        [Reactive] public ReplacePreset SelectedPreset { get; set; } = new ReplacePreset();
        public string[] Lyrics { get; private set; }

        private UVoicePart part;
        private UNote[] notes;
        private string[] startLyrics;

        public LyricsReplaceViewModel(UVoicePart part, UNote[] notes, string[] lyrics) {
            this.part = part;
            this.notes = notes;
            startLyrics = lyrics.Clone() as string[];
            Preview = string.Join(", ", lyrics);
            Lyrics = lyrics;

            this.WhenAnyValue(x => x.OldValue, x => x.NewValue)
                .Subscribe(t => {
                    Preview = Replace();
                });
            this.WhenValueChanged(x => SelectedPreset)
                .Subscribe(p => {
                    if (SelectedPreset != null) {
                        OldValue = SelectedPreset.OldValue;
                        NewValue = SelectedPreset.NewValue;
                    }
                });
        }

        public string Replace() {

            for (int i = 0; i < startLyrics.Length; i++) {
                Lyrics[i] = Regex.Replace(startLyrics[i], OldValue, NewValue);
            }
            return string.Join(", ", Lyrics);
        }

        public void Finish() {
            DocManager.Inst.StartUndoGroup();

            for (int i = 0; i < Lyrics.Length && i < notes.Length; ++i) {
                if (notes[i].lyric != Lyrics[i]) {
                    DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(part, notes[i], Lyrics[i]));
                }
            }

            DocManager.Inst.EndUndoGroup();
        }
    }

    public class ReplacePreset {
        public string Name { get; private set; } = "";
        public string OldValue { get; private set; } = "";
        public string NewValue { get; private set; } = "";

        public ReplacePreset() {

        }

        public ReplacePreset(string name, string oldValue, string newValue) {
            Name = name;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public override string ToString() {
            return Name;
        }
    }
}
