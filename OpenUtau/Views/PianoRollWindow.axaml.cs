﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.Views {
    interface IValueTip {
        void ShowValueTip();
        void HideValueTip();
        void UpdateValueTip(string text);
    }

    public partial class PianoRollWindow : Window, IValueTip {
        public MainWindow? MainWindow { get; set; }
        public readonly PianoRollViewModel ViewModel;

        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        private KeyboardPlayState? keyboardPlayState;
        private NoteEditState? editState;
        private Point valueTipPointerPosition;
        private bool shouldOpenNotesContextMenu;

        private ReactiveCommand<Unit, Unit> lyricsDialogCommand;
        private ReactiveCommand<Unit, Unit> noteDefaultsCommand;

        public PianoRollWindow() {
            InitializeComponent();
            DataContext = ViewModel = new PianoRollViewModel();
            ValueTip.IsVisible = false;

            ViewModel.LyricBatchEdits.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("lyricsreplace.replace"),
                Command = ReactiveCommand.Create(() => {
                    ReplaceLyrics();
                })
            });
            lyricsDialogCommand = ReactiveCommand.Create(() => {
                EditLyrics();
            });
            noteDefaultsCommand = ReactiveCommand.Create(() => {
                EditNoteDefaults();
            });
        }

        public void WindowDeactivated(object sender, EventArgs args) {
            LyricBox?.EndEdit();
        }

        void WindowClosing(object? sender, WindowClosingEventArgs e) {
            Hide();
            e.Cancel = true;
        }

        void OnMenuClosed(object sender, RoutedEventArgs args) {
            Focus(); // Force unfocus menu for key down events.
        }

        void OnMenuPointerLeave(object sender, PointerEventArgs args) {
            Focus(); // Force unfocus menu for key down events.
        }

        void ReplaceLyrics() {
            if (ViewModel.NotesViewModel.Selection.IsEmpty) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var (notes, lyrics) = ViewModel.NotesViewModel.PrepareInsertLyrics();
            var vm = new LyricsReplaceViewModel(ViewModel.NotesViewModel.Part, notes, lyrics);
            var dialog = new LyricsReplaceDialog() {
                DataContext = vm,
            };
            dialog.ShowDialog(this);
        }

        void OnMenuEditLyrics(object? sender, RoutedEventArgs e) {
            EditLyrics();
        }

        void EditLyrics() {
            if (ViewModel.NotesViewModel.Selection.IsEmpty) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var vm = new LyricsViewModel();
            var (notes, lyrics) = ViewModel.NotesViewModel.PrepareInsertLyrics();
            vm.Start(ViewModel.NotesViewModel.Part, notes, lyrics);
            var dialog = new LyricsDialog() {
                DataContext = vm,
            };
            dialog.ShowDialog(this);
        }

        void OnMenuNoteDefaults(object sender, RoutedEventArgs args) {
            EditNoteDefaults();
        }

        void EditNoteDefaults() {
            var dialog = new NoteDefaultsDialog();
            dialog.ShowDialog(this);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        public void OnExpButtonClick(object sender, RoutedEventArgs args) {
            var dialog = new ExpressionsDialog() {
                DataContext = new ExpressionsViewModel(),
            };
            dialog.ShowDialog(this);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        public void KeyboardPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            VScrollPointerWheelChanged(VScrollBar, args);
        }

        public void KeyboardPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (keyboardPlayState != null) {
                return;
            }
            var element = (TrackBackground)sender;
            keyboardPlayState = new KeyboardPlayState(element, ViewModel);
            keyboardPlayState.Begin(args.Pointer, args.GetPosition(element));
        }

        public void KeyboardPointerMoved(object sender, PointerEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.Update(args.Pointer, args.GetPosition(element));
            }
        }

        public void KeyboardPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.End(args.Pointer, args.GetPosition(element));
                keyboardPlayState = null;
            }
        }

        public void HScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            LyricBox?.EndEdit();
        }

        public void VScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            LyricBox?.EndEdit();
        }

        public void TimelinePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var control = (Control)sender;
            var position = args.GetCurrentPoint((Visual)sender).Position;
            var size = control.Bounds.Size;
            position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
            ViewModel.NotesViewModel.OnXZoomed(position, 0.1 * args.Delta.Y);
            LyricBox?.EndEdit();
        }

        public void ViewScalerPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            ViewModel.NotesViewModel.OnYZoomed(new Point(0, 0.5), 0.1 * args.Delta.Y);
            LyricBox?.EndEdit();
        }

        public void TimelinePointerPressed(object sender, PointerPressedEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                args.Pointer.Capture(control);
                ViewModel.NotesViewModel.PointToLineTick(point.Position, out int left, out int right);
                int tick = left + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            }
            LyricBox?.EndEdit();
        }

        public void TimelinePointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                ViewModel.NotesViewModel.PointToLineTick(point.Position, out int left, out int right);
                int tick = left + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            }
        }

        public void TimelinePointerReleased(object sender, PointerReleasedEventArgs args) {
            args.Pointer.Capture(null);
        }

        public void NotesCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                NotesCanvasLeftPointerPressed(control, point, args);
            } else if (point.Properties.IsRightButtonPressed) {
                NotesCanvasRightPointerPressed(control, point, args);
            } else if (point.Properties.IsMiddleButtonPressed) {
                editState = new NotePanningState(control, ViewModel, this);
                Cursor = ViewConstants.cursorHand;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        private void NotesCanvasLeftPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {
            if (ViewModel.NotesViewModel.DrawPitchTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                if (args.KeyModifiers == cmdKey) {
                    editState = new SmoothenPitchState(control, ViewModel, this);
                    return;
                } else {
                    editState = new DrawPitchState(control, ViewModel, this);
                    return;
                }
            }
            if (ViewModel.NotesViewModel.EraserTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(control, ViewModel, this, MouseButton.Left);
                Cursor = ViewConstants.cursorNo;
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null) {
                editState = new PitchPointEditState(control, ViewModel, this,
                    pitHitInfo.Note, pitHitInfo.Index, pitHitInfo.OnPoint, pitHitInfo.X, pitHitInfo.Y);
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit) {
                if (vbrHitInfo.hitToggle) {
                    ViewModel.NotesViewModel.ToggleVibrato(vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitStart) {
                    editState = new VibratoChangeStartState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitIn) {
                    editState = new VibratoChangeInState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitOut) {
                    editState = new VibratoChangeOutState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitDepth) {
                    editState = new VibratoChangeDepthState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitPeriod) {
                    editState = new VibratoChangePeriodState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitShift) {
                    editState = new VibratoChangeShiftState(
                        control, ViewModel, this, vbrHitInfo.note, vbrHitInfo.point, vbrHitInfo.initialShift);
                    return;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitBody) {
                if (ViewModel.NotesViewModel.KnifeTool) {
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSplitEditState(
                            control, ViewModel, this, noteHitInfo.note);
                    return;
                }
                if (noteHitInfo.hitResizeArea) {
                    editState = new NoteResizeEditState(
                        control, ViewModel, this, noteHitInfo.note,
                        args.KeyModifiers == KeyModifiers.Alt);
                    Cursor = ViewConstants.cursorSizeWE;
                } else if (args.KeyModifiers == cmdKey) {
                    ViewModel.NotesViewModel.ToggleSelectNote(noteHitInfo.note);
                } else if (args.KeyModifiers == KeyModifiers.Shift) {
                    ViewModel.NotesViewModel.SelectNotesUntil(noteHitInfo.note);
                } else {
                    editState = new NoteMoveEditState(control, ViewModel, this, noteHitInfo.note);
                    Cursor = ViewConstants.cursorSizeAll;
                }
                return;
            }
            if (ViewModel.NotesViewModel.CursorTool ||
                ViewModel.NotesViewModel.PenTool && args.KeyModifiers == cmdKey ||
                ViewModel.NotesViewModel.PenPlusTool && args.KeyModifiers == cmdKey) {
                if (args.KeyModifiers == KeyModifiers.None) {
                    // New selection.
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSelectionEditState(control, ViewModel, this, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                if (args.KeyModifiers == cmdKey) {
                    // Additional selection.
                    editState = new NoteSelectionEditState(control, ViewModel, this, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                ViewModel.NotesViewModel.DeselectNotes();
            } else if (ViewModel.NotesViewModel.PenTool ||
                ViewModel.NotesViewModel.PenPlusTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteDrawEditState(control, ViewModel, this, ViewModel.NotesViewModel.PlayTone);
            }
        }

        private void NotesCanvasRightPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {
            var selectedNotes = ViewModel.NotesViewModel.Selection.ToList();
            ViewModel.NotesViewModel.DeselectNotes();
            if (ViewModel.NotesViewModel.DrawPitchTool) {
                editState = new ResetPitchState(control, ViewModel, this);
                return;
            }
            if (ViewModel.NotesViewModel.ShowPitch) {
                var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
                if (pitHitInfo.Note != null) {
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.easeinout"),
                        Command = ViewModel.PitEaseInOutCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.linear"),
                        Command = ViewModel.PitLinearCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.easein"),
                        Command = ViewModel.PitEaseInCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.easeout"),
                        Command = ViewModel.PitEaseOutCommand,
                        CommandParameter = pitHitInfo,
                    });
                    if (pitHitInfo.OnPoint && pitHitInfo.Index == 0) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointsnapprev"),
                            Command = ViewModel.PitSnapCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    if (pitHitInfo.OnPoint && pitHitInfo.Index != 0 &&
                        pitHitInfo.Index != pitHitInfo.Note.pitch.data.Count - 1) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointdel"),
                            Command = ViewModel.PitDelCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    if (!pitHitInfo.OnPoint) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointadd"),
                            Command = ViewModel.PitAddCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    shouldOpenNotesContextMenu = true;
                    return;
                }
            }
            if (ViewModel.NotesViewModel.CursorTool || ViewModel.NotesViewModel.PenTool) {
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
                if (hitInfo.hitBody) {
                    // if note in question was already in selection before clearing
                    if (selectedNotes.Contains(hitInfo.note)) {
                        ViewModel.NotesViewModel.SelectNote(hitInfo.note, false);
                    }
                }
                if (ViewModel.NotesViewModel.Selection.Count > 0) {
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.note.copy"),
                        Command = ViewModel.NoteCopyCommand,
                        CommandParameter = hitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.note.delete"),
                        Command = ViewModel.NoteDeleteCommand,
                        CommandParameter = hitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("pianoroll.menu.notes"),
                        Items = ViewModel.NoteBatchEdits.ToArray(),
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("pianoroll.menu.lyrics"),
                        Items = ViewModel.LyricBatchEdits.ToArray(),
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("pianoroll.menu.lyrics.edit"),
                        Command = lyricsDialogCommand,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("pianoroll.menu.notedefaults"),
                        Command = noteDefaultsCommand,
                    });
                    shouldOpenNotesContextMenu = true;
                }
            } else if (ViewModel.NotesViewModel.EraserTool || ViewModel.NotesViewModel.PenPlusTool) {
                editState = new NoteEraseEditState(control, ViewModel, this, MouseButton.Right);
                Cursor = ViewConstants.cursorNo;
            }
        }

        public void NotesCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            if (ViewModel?.NotesViewModel?.HitTest == null) {
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null) {
                Cursor = ViewConstants.cursorHand;
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit) {
                if (vbrHitInfo.hitDepth) {
                    Cursor = ViewConstants.cursorSizeNS;
                } else if (vbrHitInfo.hitPeriod) {
                    Cursor = ViewConstants.cursorSizeWE;
                } else {
                    Cursor = ViewConstants.cursorHand;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitResizeArea) {
                Cursor = ViewConstants.cursorSizeWE;
                return;
            }
            Cursor = null;
        }

        public void NotesCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void NotesCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (!(sender is Control control)) {
                return;
            }
            var point = args.GetPosition(control);
            if (editState != null) {
                editState.End(args.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point);
            if (noteHitInfo.hitBody && ViewModel?.NotesViewModel?.Part != null) {
                var note = noteHitInfo.note;
                LyricBox?.Show(ViewModel.NotesViewModel.Part, new LyricBoxNote(note), note.lyric);
            }
        }

        public void NotesCanvasPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            var control = (Control)sender;
            var position = args.GetCurrentPoint(control).Position;
            var size = control.Bounds.Size;
            var delta = args.Delta;
            if (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (delta.X != 0) {
                    HScrollBar.Value = Math.Max(HScrollBar.Minimum,
                        Math.Min(HScrollBar.Maximum, HScrollBar.Value - HScrollBar.SmallChange * delta.X));
                }
                if (delta.Y != 0) {
                    VScrollBar.Value = Math.Max(VScrollBar.Minimum,
                        Math.Min(VScrollBar.Maximum, VScrollBar.Value - VScrollBar.SmallChange * delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
                ViewModel.NotesViewModel.OnYZoomed(position, 0.1 * args.Delta.Y);
            } else if (args.KeyModifiers == cmdKey) {
                TimelinePointerWheelChanged(TimelineCanvas, args);
            }
            if (editState != null) {
                var point = args.GetCurrentPoint(editState.control);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void NotesContextMenuOpening(object sender, CancelEventArgs args) {
            if (shouldOpenNotesContextMenu) {
                shouldOpenNotesContextMenu = false;
            } else {
                args.Cancel = true;
            }
        }

        public void NotesContextMenuClosing(object sender, CancelEventArgs args) {
            ViewModel.NotesContextMenuItems?.Clear();
        }

        public void ExpCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                editState = new ExpSetValueState(control, ViewModel, this);
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new ExpResetValueState(control, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position, args);
            }
        }

        public void ExpCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.Update(point.Pointer, point.Position, args);
            } else {
                Cursor = null;
            }
        }

        public void ExpCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.Update(point.Pointer, point.Position, args);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void PhonemeCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (sender is not Control control) {
                return;
            }
            var point = args.GetPosition(control);
            if (editState != null) {
                editState.End(args.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(point);
            var phoneme = hitInfo.phoneme;
            Log.Debug($"PhonemeCanvasDoubleTapped, hit = {hitInfo.hit}, point = {{{hitInfo.point}}}, phoneme = {phoneme?.phoneme}");
            if (!hitInfo.hit) {
                return;
            }
            LyricBox?.Show(ViewModel.NotesViewModel.Part, new LyricBoxPhoneme(phoneme!), phoneme!.phoneme);
        }

        public void PhonemeCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                if (args.KeyModifiers == cmdKey) {
                    var hitAliasInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(args.GetPosition(control));
                    if (hitAliasInfo.hit) {
                        var singer = ViewModel.NotesViewModel.Project.tracks[ViewModel.NotesViewModel.Part.trackNo].Singer;
                        if (Core.Util.Preferences.Default.OtoEditor == 1 && !string.IsNullOrEmpty(Core.Util.Preferences.Default.VLabelerPath)) {
                            Integrations.VLabelerClient.Inst.GotoOto(singer, hitAliasInfo.phoneme.oto);
                        } else {
                            MainWindow?.OpenSingersWindow();
                            Core.DocManager.Inst.ExecuteCmd(new Core.GotoOtoNotification(singer, hitAliasInfo.phoneme.oto));
                        }
                        return;
                    }
                }
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
                if (hitInfo.hit) {
                    var phoneme = hitInfo.phoneme;
                    var note = phoneme.Parent;
                    var index = phoneme.index;
                    if (hitInfo.hitPosition) {
                        editState = new PhonemeMoveState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitPreutter) {
                        editState = new PhonemeChangePreutterState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitOverlap) {
                        editState = new PhonemeChangeOverlapState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    }
                }
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new PhonemeResetState(control, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void PhonemeCanvasPointerMoved(object sender, PointerEventArgs args) {
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            var aliasHitInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(point.Position);
            if (aliasHitInfo.hit) {
                ViewModel.MouseoverPhoneme(aliasHitInfo.phoneme);
                Cursor = null;
                return;
            }
            var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
            if (hitInfo.hit) {
                Cursor = ViewConstants.cursorSizeWE;
                ViewModel.MouseoverPhoneme(null);
                return;
            }
            ViewModel.MouseoverPhoneme(null);
            Cursor = null;
        }

        public void PhonemeCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void OnSnapDivMenuButton(object sender, RoutedEventArgs args) {
            SnapDivMenu.PlacementTarget = sender as Button;
            SnapDivMenu.Open();
        }

        void OnSnapDivKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) {
                if (sender is ContextMenu menu && menu.SelectedItem is MenuItemViewModel item) {
                    item.Command?.Execute(item.CommandParameter);
                }
            }
        }

        #region value tip

        void IValueTip.ShowValueTip() {
            if (ValueTip != null) {
                ValueTip.IsVisible = true;
            }
        }

        void IValueTip.HideValueTip() {
            if (ValueTip != null) {
                ValueTip.IsVisible = false;
            }
            if (ValueTipText != null) {
                ValueTipText.Text = string.Empty;
            }
        }

        void IValueTip.UpdateValueTip(string text) {
            if (ValueTip == null || ValueTipText == null || ValueTipCanvas == null) {
                return;
            }
            ValueTipText.Text = text;
            Canvas.SetLeft(ValueTip, valueTipPointerPosition.X);
            double tipY = valueTipPointerPosition.Y + 21;
            if (tipY + 21 > ValueTipCanvas!.Bounds.Height) {
                tipY = tipY - 42;
            }
            Canvas.SetTop(ValueTip, tipY);
        }

        #endregion

        void OnKeyDown(object sender, KeyEventArgs args) {
            if (LyricBox != null && LyricBox.IsVisible) {
                args.Handled = false;
                return;
            }
            var notesVm = ViewModel.NotesViewModel;
            if (notesVm.Part == null) {
                args.Handled = false;
                return;
            }

            // returns true if handled
            args.Handled = OnKeyExtendedHandler(args);
        }

        bool OnKeyExtendedHandler(KeyEventArgs args) {
            var notesVm = ViewModel.NotesViewModel;
            var playVm = ViewModel.PlaybackViewModel;
            if (notesVm?.Part == null || playVm == null) {
                return false;
            }
            var project = Core.DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int deltaTicks = notesVm.IsSnapOn ? snapUnit : 15;

            bool isNone = args.KeyModifiers == KeyModifiers.None;
            bool isAlt = args.KeyModifiers == KeyModifiers.Alt;
            bool isCtrl = args.KeyModifiers == cmdKey;
            bool isShift = args.KeyModifiers == KeyModifiers.Shift;
            bool isBoth = args.KeyModifiers == (cmdKey | KeyModifiers.Shift);

            switch (args.Key) {
                #region document keys
                case Key.Space:
                    if (isNone) {
                        try {
                            playVm.PlayOrPause();
                        } catch (Exception e) {
                            MessageBox.ShowError(this, e);
                        }
                        return true;
                    }
                    break;
                case Key.Escape:
                    if (isNone) {
                        // collapse/empty selection
                        var numSelected = notesVm.Selection.Count;
                        // if single or all notes then clear
                        if (numSelected == 1 || numSelected == notesVm.Part.notes.Count) {
                            notesVm.DeselectNotes();
                        } else if (numSelected > 1) {
                            // collapse selection
                            notesVm.SelectNote(notesVm.Selection.Head!);
                        }
                        return true;
                    }
                    break;
                case Key.F4:
                    if (isAlt) {
                        Hide();
                        return true;
                    }
                    break;
                case Key.Enter:
                    if (isNone) {
                        if (notesVm.Selection.Count == 1) {
                            var note = notesVm.Selection.First();
                            LyricBox?.Show(ViewModel.NotesViewModel.Part!, new LyricBoxNote(note), note.lyric);
                        } else if (notesVm.Selection.Count > 1) {
                            EditLyrics();
                        }
                        return true;
                    }
                    break;
                #endregion
                #region tool select keys
                // TOOL SELECT
                case Key.D1:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("1").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector1?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D2:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("2").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector2?.SelectExp();
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.SelectToolCommand?.Execute("2+").Subscribe();
                        return true;
                    }
                    break;
                case Key.D3:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("3").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector3?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D4:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("4").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector4?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D5:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("5").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector5?.SelectExp();
                        return true;
                    }
                    break;
                #endregion
                #region toggle show keyws
                case Key.R:
                    if (isNone) {
                        notesVm.ShowFinalPitch = !notesVm.ShowFinalPitch;
                        return true;
                    }
                    break;
                case Key.T:
                    if (isNone) {
                        notesVm.ShowTips = !notesVm.ShowTips;
                        return true;
                    }
                    break;
                case Key.U:
                    if (isNone) {
                        notesVm.ShowVibrato = !notesVm.ShowVibrato;
                        return true;
                    }
                    break;
                case Key.I:
                    if (isNone) {
                        notesVm.ShowPitch = !notesVm.ShowPitch;
                        return true;
                    }
                    break;
                case Key.O:
                    if (isNone) {
                        notesVm.ShowPhoneme = !notesVm.ShowPhoneme;
                        return true;
                    }
                    break;
                case Key.P:
                    if (isNone) {
                        notesVm.IsSnapOn = !notesVm.IsSnapOn;
                        return true;
                    }
                    if (isAlt) {
                        SnapDivMenu.Open();
                    }
                    break;
                case Key.OemPipe:
                    if (isNone) {
                        notesVm.ShowNoteParams = !notesVm.ShowNoteParams;
                        return true;
                    }
                    break;
                #endregion
                #region navigate keys
                // NAVIGATE/EDIT/SELECT HANDLERS
                case Key.Up:
                    if (isNone) {
                        notesVm.TransposeSelection(1);
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.TransposeSelection(12);
                        return true;
                    }
                    break;
                case Key.Down:
                    if (isNone) {
                        notesVm.TransposeSelection(-1);
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.TransposeSelection(-12);
                        return true;
                    }
                    break;
                case Key.Left:
                    if (isNone) {
                        notesVm.MoveCursor(-1);
                        return true;
                    }
                    if (isAlt) {
                        notesVm.ResizeSelectedNotes(-1 * deltaTicks);
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.MoveSelectedNotes(-1 * deltaTicks);
                        return true;
                    }
                    if (isShift) {
                        notesVm.ExtendSelection(-1);
                        return true;
                    }
                    break;
                case Key.Right:
                    if (isNone) {
                        notesVm.MoveCursor(1);
                        return true;
                    }
                    if (isAlt) {
                        notesVm.ResizeSelectedNotes(deltaTicks);
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.MoveSelectedNotes(deltaTicks);
                        return true;
                    }
                    if (isShift) {
                        notesVm.ExtendSelection(1);
                        return true;
                    }
                    break;
                case Key.OemPlus:
                    if (isNone) {
                        notesVm.ResizeSelectedNotes(deltaTicks);
                        return true;
                    }
                    break;
                case Key.OemMinus:
                    if (isNone) {
                        notesVm.ResizeSelectedNotes(-1 * deltaTicks);
                        return true;
                    }
                    break;
                #endregion
                #region clipboard and edit keys
                case Key.Z:
                    if (isBoth) {
                        ViewModel.Redo();
                        return true;
                    }
                    if (isCtrl) {
                        ViewModel.Undo();
                        return true;
                    }
                    break;
                case Key.Y:
                    // toggle play tone
                    if (isNone) {
                        notesVm.PlayTone = !notesVm.PlayTone;
                        return true;
                    }
                    if (isCtrl) {
                        ViewModel.Redo();
                        return true;
                    }
                    break;
                case Key.C:
                    if (isCtrl) {
                        notesVm.CopyNotes();
                        return true;
                    }
                    break;
                case Key.X:
                    if (isCtrl) {
                        notesVm.CutNotes();
                        return true;
                    }
                    break;
                case Key.V:
                    if (isCtrl) {
                        notesVm.PasteNotes();
                        return true;
                    }
                    break;
                // INSERT + DELETE
                case Key.Insert:
                    if (isNone) {
                        notesVm.InsertNote();
                        return true;
                    }
                    break;
                case Key.Delete:
                case Key.Back:
                    if (isNone) {
                        notesVm.DeleteSelectedNotes();
                        return true;
                    }
                    break;
                #endregion
                #region play position and select keys
                // PLAY POSITION + SELECTION
                case Key.Home:
                    if (isNone) {
                        playVm.MovePlayPos(notesVm.Part.position);
                        return true;
                    }
                    if (isShift) {
                        notesVm.ExtendSelection(notesVm.Part.notes.FirstOrDefault());
                        return true;
                    }
                    break;
                case Key.End:
                    if (isNone) {
                        playVm.MovePlayPos(notesVm.Part.End);
                        return true;
                    }
                    if (isShift) {
                        notesVm.ExtendSelection(notesVm.Part.notes.LastOrDefault());
                        return true;
                    }
                    break;
                case Key.OemOpenBrackets:
                    // move playhead left
                    if (isNone) {
                        playVm.MovePlayPos(playVm.PlayPosTick - snapUnit);
                        return true;
                    }
                    // to selection start
                    if (isCtrl) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position);
                        }
                        return true;
                    }
                    // to view start
                    if (isShift) {
                        playVm.MovePlayPos(notesVm.Part.position + (int)notesVm.TickOffset);
                        return true;
                    }
                    break;
                case Key.OemCloseBrackets:
                    // move playhead right
                    if (isNone) {
                        playVm.MovePlayPos(playVm.PlayPosTick + snapUnit);
                        return true;
                    }
                    // to selection end
                    if (isCtrl) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.LastOrDefault()!.RightBound);
                        }
                        return true;
                    }
                    // to view end
                    if (isShift) {
                        playVm.MovePlayPos(notesVm.Part.position + (int)(notesVm.TickOffset));
                        return true;
                    }
                    break;

                #endregion
                #region scroll and select keys
                // SCROLL / SELECT
                case Key.A:
                    // scroll left
                    if (isNone) {
                        notesVm.TickOffset = Math.Max(0, notesVm.TickOffset - snapUnit);
                        return true;
                    }
                    // select all
                    if (isCtrl) {
                        notesVm.SelectAllNotes();
                        return true;
                    }
                    break;
                case Key.D:
                    // scroll right
                    if (isNone) {
                        notesVm.TickOffset = Math.Min(notesVm.TickOffset + snapUnit, notesVm.HScrollBarMax);
                        return true;
                    }
                    // select none
                    if (isCtrl) {
                        notesVm.SelectAllNotes();
                        return true;
                    }
                    break;
                case Key.W:
                    // toggle show waveform
                    if (isNone) {
                        notesVm.ShowWaveform = !notesVm.ShowWaveform;
                        return true;
                    }
                    // scroll up
                    // NOTE set to alt to avoid conflict with showwaveform toggle
                    if (isAlt) {
                        notesVm.TrackOffset = Math.Max(notesVm.TrackOffset - 2, 0);
                        return true;
                    }
                    break;
                case Key.S:
                    // scroll down
                    if (isAlt) {
                        notesVm.TrackOffset = Math.Min(notesVm.TrackOffset + 2, notesVm.VScrollBarMax);
                        return true;
                    }
                    if (isCtrl) {
                        _ = MainWindow?.Save();
                        return true;
                    }
                    break;
                case Key.F:
                    // scroll selection into focus
                    if (isNone) {
                        var note = notesVm.Selection.FirstOrDefault();
                        if (note != null) {
                            DocManager.Inst.ExecuteCmd(new FocusNoteNotification(notesVm.Part, note));
                        }
                        return true;
                    }
                    if (isCtrl) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position);
                        }
                        return true;
                    }
                    break;
                case Key.E:
                    // zoom in
                    if (isNone) {
                        double x = 0;
                        double y = 0;
                        if (!notesVm.Selection.IsEmpty) {
                            x = (notesVm.Selection.Head!.position - notesVm.TickOffset) / notesVm.ViewportTicks;
                            y = (ViewConstants.MaxTone - 1 - notesVm.Selection.Head.tone - notesVm.TrackOffset) / notesVm.ViewportTracks;
                        } else if (notesVm.TickOffset != 0) {
                            x = 0.5;
                            y = 0.5;
                        }
                        notesVm.OnXZoomed(new Point(x, y), 0.1);
                        return true;
                    }
                    break;
                case Key.Q:
                    // zoom out
                    if (isNone) {
                        double x = 0;
                        double y = 0;
                        if (!notesVm.Selection.IsEmpty) {
                            x = (notesVm.Selection.Head!.position - notesVm.TickOffset) / notesVm.ViewportTicks;
                            y = (ViewConstants.MaxTone - 1 - notesVm.Selection.Head.tone - notesVm.TrackOffset) / notesVm.ViewportTracks;
                        } else if (notesVm.TickOffset != 0) {
                            x = 0.5;
                            y = 0.5;
                        }
                        notesVm.OnXZoomed(new Point(x, y), -0.1);
                        return true;
                    }
                    break;
                    #endregion
            }
            return false;
        }

        public string[] CacheExpressions() {
            NotesViewModel vm = (DataContext as PianoRollViewModel)!.NotesViewModel;
            return new string[] { vm.SecondaryKey, vm.PrimaryKey };
        }
        public void LoadCacheExpressions(string[] cache) {
            if (expSelector1 == null) {
                return;
            }
            foreach (string key in cache) {

                UExpressionDescriptor exp = (expSelector1.DataContext as ExpSelectorViewModel).Descriptor;
                if (exp != null && exp.abbr == key) {
                    expSelector1.SelectExp();
                    continue;
                }
                exp = (expSelector2.DataContext as ExpSelectorViewModel).Descriptor;
                if (exp != null && exp.abbr == key) {
                    expSelector2.SelectExp();
                    continue;
                }
                exp = (expSelector3.DataContext as ExpSelectorViewModel).Descriptor;
                if (exp != null && exp.abbr == key) {
                    expSelector3.SelectExp();
                    continue;
                }
                exp = (expSelector4.DataContext as ExpSelectorViewModel).Descriptor;
                if (exp != null && exp.abbr == key) {
                    expSelector4.SelectExp();
                    continue;
                }
                exp = (expSelector5.DataContext as ExpSelectorViewModel).Descriptor;
                if (exp != null && exp.abbr == key) {
                    expSelector5.SelectExp();
                    continue;
                }
            }
        }
    }
}
