﻿using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NWaves.Audio;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class SingersDialog : Window, ICmdSubscriber {
        private bool editingCell = false;

        WaveFile? wav;
        string? wavPath;

        public SingersDialog() {
            InitializeComponent();
            DocManager.Inst.AddSubscriber(this);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            DocManager.Inst.RemoveSubscriber(this);
        }

        void OnSingerMenuButton(object sender, RoutedEventArgs args) {
            SingerMenu.PlacementTarget = sender as Button;
            SingerMenu.Open();
        }

        void OnVisitWebsite(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            try {
                OS.OpenWeb(viewModel.Singer.Web);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        async void OnSetPortrait(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var dialog = new OpenFileDialog() {
                AllowMultiple = false,
                Directory = viewModel.Singer.Location,
            };
            var files = await dialog.ShowAsync(this);
            if (files == null || files.Length != 1) {
                return;
            }
            try {
                using (var stream = File.OpenRead(files[0])) {
                    var portrait = new Bitmap(stream);
                    portrait.Dispose();
                }
                viewModel.SetPortrait(Path.GetRelativePath(viewModel.Singer.Location, files[0]));
            } catch (Exception e) {
                Log.Error(e, "Failed to set portrait");
                _ = await MessageBox.ShowError(this, e);
            }
        }

        async void OnEditSubbanksButton(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var dialog = new EditSubbanksDialog();
            dialog.ViewModel.SetSinger(viewModel.Singer!);
            dialog.RefreshSinger = () => viewModel.RefreshSinger();
            await dialog.ShowDialog(this);
        }

        void OnSelectedSingerChanged(object sender, SelectionChangedEventArgs e) {
            OtoPlot.WaveFile = null;
        }

        void OnSelectedOtoChanged(object sender, SelectionChangedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null || e.AddedItems.Count < 1) {
                return;
            }
            var oto = (UOto?)e.AddedItems[0];
            if (oto == null || !File.Exists(oto.File)) {
                return;
            }
            DrawOto(oto);
        }

        void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e) {
            editingCell = true;
        }

        void OnCellEditEnded(object sender, DataGridCellEditEndedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (e.EditAction == DataGridEditAction.Commit) {
                viewModel?.NotifyOtoChanged();
            }
            editingCell = false;
        }

        void GotoSourceFile(object sender, RoutedEventArgs args) {
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            try {
                OS.GotoFile(oto.File);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void GotoVLabelerOto(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            if (viewModel.Singer != null) {
                OpenInVLabeler(viewModel.Singer, oto);
            }
        }

        void OnEditInVLabeler(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer != null) {
                OpenInVLabeler(viewModel.Singer, null);
            }
        }

        private void OpenInVLabeler(USinger singer, UOto? oto) {
            string path = Core.Util.Preferences.Default.VLabelerPath;
            if (string.IsNullOrEmpty(path) || !OS.AppExists(path)) {
                MessageBox.Show(
                    this,
                    ThemeManager.GetString("singers.editoto.setvlabelerpath"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            try {
                Integrations.VLabelerClient.Inst.GotoOto(singer, oto);
            } catch (Exception e) {
                MessageBox.Show(
                    this,
                    e.ToString(),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
            }
        }

        void RegenFrq(object sender, RoutedEventArgs args) {
            if (OtoGrid != null &&
                sender is Control control &&
                DataContext is SingersViewModel viewModel) {
                string[] files = OtoGrid.SelectedItems
                    .Cast<UOto>()
                    .Select(oto => oto.File)
                    .ToHashSet()
                    .ToArray();
                MessageBox? msgbox = null;
                string text = ThemeManager.GetString("singers.editoto.regenfrq.regenerating");
                if (files.Length > 1) {
                    msgbox = MessageBox.ShowModal(this, text, text);
                }
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                viewModel.RegenFrq(files, control.Tag as string, count => {
                    msgbox?.SetText(string.Format("{0}\n{1} / {2}", text, count, files.Length));
                }).ContinueWith(task => {
                    msgbox?.Close();
                    if (task.IsFaulted && task.Exception != null) {
                        MessageBox.ShowError(this, task.Exception);
                    } else {
                        DrawOto(viewModel.SelectedOto);
                    }
                }, scheduler);
            }
        }

        void DrawOto(UOto? oto) {
            if (oto == null) {
                wavPath = null;
                wav = null;
                OtoPlot.WaveFile = null;
                OtoPlot.F0 = null;
                return;
            }
            OtoPlot.Cutoff = oto.Cutoff;
            OtoPlot.Offset = oto.Offset;
            OtoPlot.Consonant = oto.Consonant;
            OtoPlot.Preutter = oto.Preutter;
            OtoPlot.Overlap = oto.Overlap;
            OtoPlot.WaveFile = loadWav(oto);
            OtoPlot.F0 = LoadF0(oto.File);
        }

        WaveFile? loadWav(UOto oto) {
            if (wavPath == oto.File) {
                return wav;
            }
            try {
                using (var memStream = new MemoryStream()) {
                    using (var waveStream = Core.Format.Wave.OpenFile(oto.File)) {
                        NAudio.Wave.WaveFileWriter.WriteWavFileToStream(memStream, waveStream);
                    }
                    memStream.Seek(0, SeekOrigin.Begin);
                    wav = new WaveFile(memStream);
                    wavPath = oto.File;
                    return wav;
                }
            } catch (Exception e) {
                Log.Error(e, "failed to load wav");
            }
            return null;
        }

        Tuple<int, double[]>? LoadF0(string wavPath) {
            string frqFile = Classic.VoicebankFiles.GetFrqFile(wavPath);
            if (!File.Exists(frqFile)) {
                return null;
            }
            var frq = new Classic.Frq();
            using (var fileStream = File.OpenRead(frqFile)) {
                frq.Load(fileStream);
            }
            return Tuple.Create(frq.hopSize, frq.f0);
        }

        void OnKeyDown(object sender, KeyEventArgs args) {
            if (args.Handled || editingCell || (FocusManager?.GetFocusedElement() is TextBox)) {
                return;
            }
            var viewModel = DataContext as SingersViewModel;
            if (viewModel == null || OtoPlot == null || OtoPlot.WaveFile == null) {
                return;
            }
            double durationMs = OtoPlot.WaveFile.Signals[0].Duration * 1000;
            args.Handled = true;
            switch (args.Key) {
                case Key.D1:
                    viewModel.SetOffset(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D2:
                    viewModel.SetOverlap(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D3:
                    viewModel.SetPreutter(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D4:
                    viewModel.SetFixed(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D5:
                    viewModel.SetCutoff(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.W:
                    OtoPlot.Zoom(0.5, 0.5);
                    break;
                case Key.S:
                    OtoPlot.Zoom(1.5, 0.5);
                    break;
                case Key.A:
                    OtoPlot.Pan(-0.25);
                    break;
                case Key.D:
                    OtoPlot.Pan(0.25);
                    break;
                case Key.Q:
                    if (OtoGrid != null) {
                        OtoGrid.SelectedIndex = Math.Max(0, OtoGrid.SelectedIndex - 1);
                        OtoGrid.ScrollIntoView(OtoGrid.SelectedItem, null);
                    }
                    break;
                case Key.E:
                    if (OtoGrid != null) {
                        OtoGrid.SelectedIndex++;
                        OtoGrid.ScrollIntoView(OtoGrid.SelectedItem, null);
                    }
                    break;
                case Key.F:
                    OtoPlot.Zoom(double.PositiveInfinity, 0.5);
                    break;
                default:
                    args.Handled = false;
                    break;
            }
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is OtoChangedNotification otoChanged) {
                var viewModel = DataContext as SingersViewModel;
                if (viewModel == null) {
                    return;
                }
                if (otoChanged.external) {
                    viewModel.RefreshSinger();
                }
                DrawOto(viewModel.SelectedOto);
            } else if (cmd is GotoOtoNotification editOto) {
                var viewModel = DataContext as SingersViewModel;
                if (viewModel == null) {
                    return;
                }
                viewModel.GotoOto(editOto.singer, editOto.oto);
                OtoGrid?.ScrollIntoView(OtoGrid.SelectedItem, null);
                Activate();
            }
        }

        #endregion
    }
}
