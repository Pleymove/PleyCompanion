// Ports Dictation/UI/SettingsDictationPage.swift and the key rows of SettingsKeyboardPage.swift:
// everything about typing with your voice, in the order someone sets it up. The key you hold (with
// a sketch of where it is), who hears you, in which language, whether AI tidies the words, through
// which microphone, when a quiet take ends, and the tones. Each engine says where the audio goes;
// its key lives under Keys & Account. (No fn rows and no haptics on Windows.)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Platform;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shortcuts;
using OpenClicky.Core.Speech;
using OpenClicky.Windows.Platform;
using WpfUi = Wpf.Ui.Controls;

namespace OpenClicky.Windows.AppWindow;

internal sealed partial class SettingsRowFactory
{
    /// <summary>The key picker's label ("Right Ctrl").</summary>
    public static string DictationKeyTitle(DictationHotkey key) => key switch
    {
        DictationHotkey.RightControl => "Right Ctrl",
        DictationHotkey.LeftControl => "Left Ctrl",
        DictationHotkey.RightAlt => "Right Alt (AltGr)",
        DictationHotkey.RightShift => "Right Shift",
        _ => "Caps Lock",
    };

    /// <summary>The language the "Pin one" choice comes back to (same key the Mac's Styles page used).</summary>
    private const string LastPinnedLanguageKey = "dictation.lastPinnedLanguage";

    private UIElement? DictationRow(SettingsItem item, string query)
    {
        if (item.Id.StartsWith("dictation.engine.", StringComparison.Ordinal))
        {
            var choice = DictationEngineChoices.FromRawValue(item.Id["dictation.engine.".Length..]) ?? DictationEngineChoice.Offline;
            return EngineOption(choice, query);
        }
        switch (item.Id)
        {
            case "keyboard.dictationKey":
                return LiveSettingsCard(() => Ui.Row(item.Title, SettingsCatalog.DictationKeyDetail(Settings.DictationKey), query,
                    Ui.Picker(DictationHotkeys.AllCases.Select(key => (key, DictationKeyTitle(key))), Settings.DictationKey, key => Settings.DictationKey = key, 170)));
            case "keyboard.tapKeepsListening":
                return LiveSettingsCard(() => Ui.Row(item.Title, item.Detail, query,
                    Ui.Toggle(Settings.TapKeepsListening, isOn => Settings.TapKeepsListening = isOn)));
            case "keyboard.where":
                return new LiveContent(() => Ui.Card(KeyboardSketch.WithCaption(Settings.DictationKey, animates: !Settings.ReduceAnimation)),
                    Changes.Of(Settings, nameof(DictationSettings.DictationKey), nameof(DictationSettings.ReduceAnimation)));
            case "dictation.language":
                return LiveSettingsCard(() => DictationLanguageRow(item, query));
            case "dictation.script":
                return LiveSettingsCard(() => Ui.Row(item.Title, "Same words, written two ways. The AI clean-up writes it.", query,
                    Ui.Picker(new[]
                    {
                        (DictationScript.Native, "Native · नमस्ते, आप कैसे हैं?"),
                        (DictationScript.Roman, "Roman · namaste, aap kaise hain?"),
                    }, Settings.Script, script => Settings.Script = script, 220)));
            case "dictation.cleanUp":
                return new LiveContent(() => Ui.Card(Ui.Row(item.Title, DictationCleanUp.Detail(hasModel: DictationEngineResolver.MakePolisher() is not null), query,
                    Ui.Toggle(DictationCleanUp.SwitchShowsOn(Settings), isOn => DictationCleanUp.SetSwitch(Settings, isOn)))),
                    Changes.Of(Settings), Changes.Shell);
            case "dictation.input":
                return new LiveContent(() => Ui.Card(MicrophoneInputRow(item, query)),
                    Changes.Of(Settings, nameof(DictationSettings.PreferredMicrophoneUID)),
                    Changes.Event(handler => services.Microphones.DevicesChanged += handler, handler => services.Microphones.DevicesChanged -= handler));
            case "dictation.micTest":
                return Ui.Card(new VoiceDiagnosticsRow(item, query, services));
            case "dictation.inactivityTimeout":
                return LiveSettingsCard(() => Ui.Row(item.Title, item.Detail, query,
                    Ui.Picker(new[] { (1, "1 minute"), (3, "3 minutes"), (5, "5 minutes"), (10, "10 minutes"), (0, "Never") },
                        Settings.InactivityTimeoutMinutes, minutes => Settings.InactivityTimeoutMinutes = minutes, 140)));
            case "dictation.pasteLast":
                return Ui.Card(Ui.Row(item.Title, item.Detail, query, Ui.Button("Paste Now", services.History.PasteLast)));
            case "dictation.sounds":
                return SettingsToggleRow(item.Title, item.Detail, query, () => Settings.Sounds, isOn => Settings.Sounds = isOn, nameof(DictationSettings.Sounds));
            default:
                return null;
        }
    }

    // MARK: - who hears you

    /// <summary>
    /// One engine as a radio option: its name and tag, what it does, and on the right what it still
    /// needs. The chosen engine that can't run yet says why, with the way to fix it.
    /// </summary>
    private LiveContent EngineOption(DictationEngineChoice choice, string query)
    {
        var models = (services.VoiceModels?.Models ?? []).OfType<ParakeetDictation>()
            .Select(model => Changes.Of(model)).ToArray();
        return new LiveContent(() =>
        {
            var isSelected = Settings.Engine == choice;
            var unavailableReason = DictationEngineResolver.UnavailableReason(choice);
            UIElement? badge = null;
            if (unavailableReason is not null && choice.UnavailableBadge().Length > 0)
            {
                var badgeText = Ui.Text(choice.UnavailableBadge(), SettingsLook.CaptionSize, isSelected ? SettingsLook.Danger : SettingsLook.TextSecondary, wrap: false);
                badgeText.ToolTip = unavailableReason;
                badge = badgeText;
            }
            var title = choice == DictationEngineChoice.Parakeet ? ParakeetDictation.SelectedModel.Info.Name : choice.SettingsTitle();
            var option = Ui.RadioOption(isSelected, title, choice.SettingsDetail(), query, () =>
            {
                Settings.Engine = choice;
            }, Ui.Tag(choice.AudioDestinationTag(), choice.IsOnDevice()), badge);
            var stack = new StackPanel();
            stack.Children.Add(option);
            if (choice == DictationEngineChoice.Parakeet && isSelected)
            {
                stack.Children.Add(Ui.Row("Modèles locaux", unavailableReason ?? "Choisissez le modèle partagé avec l’assistant.", "",
                    Ui.Button("Gérer", () => actions.ShowSettings(DictationSettingsPage.Recognition))));
            }
            if (isSelected && unavailableReason is not null && choice != DictationEngineChoice.Parakeet)
            {
                var reason = Ui.Text(WordsPageSentences.SentenceCased(unavailableReason) + ".", SettingsLook.CaptionSize, SettingsLook.Danger);
                if (choice == DictationEngineChoice.Moonshine)
                {
                    stack.Children.Add(reason);
                    return Ui.Card(stack);
                }
                var fix = choice == DictationEngineChoice.Sarvam
                    ? Ui.Button("Add Key…", () => actions.ShowSettings(DictationSettingsPage.Account))
                    : Ui.Button("Sign In…", () => actions.ShowSettings(DictationSettingsPage.Account));
                var fixRow = new DockPanel { Margin = new Thickness(30, 10, 0, 0), LastChildFill = true };
                DockPanel.SetDock(fix, Dock.Right);
                fix.Margin = new Thickness(12, 0, 0, 0);
                fixRow.Children.Add(fix);
                reason.VerticalAlignment = VerticalAlignment.Center;
                fixRow.Children.Add(reason);
                stack.Children.Add(fixRow);
            }
            return Ui.Card(stack);
        }, [Changes.Of(Settings, nameof(DictationSettings.Engine), nameof(DictationSettings.LanguageCode)), Changes.Shell, .. models]);
    }

    // MARK: - language

    /// <summary>Auto-detect or one pinned language, from the languages the region offers. Pinning again comes back to the language last pinned.</summary>
    private Grid DictationLanguageRow(SettingsItem item, string query)
    {
        var isAuto = Settings.Language == DictationLanguage.Auto;
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Ui.Picker(new[] { (true, "Auto-detect"), (false, "Pin one") }, isAuto, chooseAuto =>
        {
            if (chooseAuto) Settings.LanguageCode = DictationLanguage.Auto.Code;
            else if (Settings.Language == DictationLanguage.Auto) Settings.LanguageCode = LanguageToPin().Code;
        }, 130));
        if (!isAuto)
        {
            var languages = DictationLanguage.PinnableChoices(Settings.Region, Settings.Language)
                .Select(language => (language.Code, language.Name == language.NativeName ? language.Name : $"{language.Name} · {language.NativeName}"));
            var languagePicker = Ui.Picker(languages, Settings.LanguageCode, code =>
            {
                Settings.LanguageCode = code;
                services.Preferences.Set(LastPinnedLanguageKey, code);
            }, 170);
            languagePicker.Margin = new Thickness(8, 0, 0, 0);
            controls.Children.Add(languagePicker);
        }
        return Ui.Row(item.Title, item.Detail, query, controls);
    }

    /// <summary>The last pinned language if the region still offers it, else the region's English.</summary>
    private DictationLanguage LanguageToPin()
    {
        var lastPinned = DictationLanguage.Named(services.Preferences.GetString(LastPinnedLanguageKey));
        var offered = DictationLanguage.PinnableChoices(Settings.Region, DictationLanguage.Auto);
        return offered.Contains(lastPinned) ? lastPinned : DictationLanguage.DefaultPinned(Settings.Region);
    }

    // MARK: - microphone

    private Grid MicrophoneInputRow(SettingsItem item, string query)
    {
        var microphones = services.Microphones;
        var devices = microphones.Available();
        var automaticName = microphones.EffectiveInputName(null) ?? "none";
        var chosen = Settings.PreferredMicrophoneUID is { } chosenId ? devices.FirstOrDefault(device => device.Id == chosenId) : null;
        var currentName = chosen?.Name ?? microphones.EffectiveInputName(null) ?? "no microphone found";
        var options = new List<(string Id, string Title)> { ("", $"Automatic ({automaticName})") };
        options.AddRange(devices.Select(device => (device.Id, device.Name)));
        // A chosen microphone that isn't connected stays listed, so the picker never shows a blank.
        if (Settings.PreferredMicrophoneUID is { } missingId && chosen is null) options.Add((missingId, "Not connected"));
        var picker = Ui.Picker(options, Settings.PreferredMicrophoneUID ?? "", id => Settings.PreferredMicrophoneUID = id.Length == 0 ? null : id, 200, maxWidth: 280);
        return Ui.Row(item.Title, $"Automatic follows Windows’ default microphone. Clicky listens through it too. Now: {currentName}.", query, picker);
    }

    private sealed class VoiceDiagnosticsRow : ContentControl
    {
        private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(5);
        private readonly SettingsItem item;
        private readonly string query;
        private readonly MainWindowServices services;
        private readonly bool transcriptionOnly;
        private readonly ProgressBar meter = new() { Minimum = 0, Maximum = 1, Width = 80, Height = 8 };
        private CancellationTokenSource? currentTest;

        public VoiceDiagnosticsRow(SettingsItem item, string query, MainWindowServices services, bool transcriptionOnly = false)
        {
            this.item = item;
            this.query = query;
            this.services = services;
            this.transcriptionOnly = transcriptionOnly;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Render(item.Detail);
            Unloaded += (_, _) => Cancel();
            IsVisibleChanged += (_, _) => { if (!IsVisible) Cancel(); };
        }

        private void Render(string message, bool failed = false)
        {
            var controls = currentTest is null
                ? Ui.HStack(8, transcriptionOnly ? null : Ui.Button("Microphone", () => _ = RunAsync(transcribe: false)),
                    Ui.Button(transcriptionOnly ? "Enregistrer" : "Transcription", () => _ = RunAsync(transcribe: true)))
                : Ui.HStack(8, Ui.Button("Annuler", Cancel));
            if (meter.Parent is Panel previousPanel) previousPanel.Children.Remove(meter);
            var stack = new StackPanel();
            stack.Children.Add(Ui.Row(item.Title, message, query, controls, failed ? SettingsLook.Danger : SettingsLook.TextSecondary));
            if (currentTest is not null) stack.Children.Add(meter);
            Content = stack;
        }

        private async Task RunAsync(bool transcribe)
        {
            if (currentTest is not null) return;
            if (transcriptionOnly && !services.Settings.Engine.KeepsVoiceOnThisPC())
            {
                Render("Sélectionnez un modèle local ci-dessus pour enregistrer et transcrire sur ce PC.", failed: true);
                return;
            }
            if (services.IsVoiceInputBusy())
            {
                Render("Terminez l’activité vocale ou désactivez le mode mains libres avant le test.", failed: true);
                return;
            }
            if (services.MicrophonePermission.ReadConsent() is MicrophoneConsent.DeniedForDevice
                or MicrophoneConsent.DeniedForAllApps or MicrophoneConsent.DeniedForDesktopApps)
            {
                Render("Autorisez le microphone dans Paramètres Windows → Confidentialité et sécurité → Microphone.", failed: true);
                return;
            }

            using var test = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            currentTest = test;
            meter.Value = 0;
            Render("Parlez pendant cinq secondes.");
            var message = "Test annulé ou délai dépassé.";
            var failed = false;
            try
            {
                message = transcribe ? await TestTranscriptionAsync(test.Token) : await TestMicrophoneAsync(test.Token);
            }
            catch (OperationCanceledException) when (test.IsCancellationRequested)
            {
                // Closing or hiding Settings takes the microphone with it.
            }
            catch (Exception error)
            {
                failed = true;
                message = error.Message;
                AppLog.Write($"voice diagnostics: failed ({error.GetType().Name})");
            }
            finally
            {
                currentTest = null;
                Render(message, failed);
            }
        }

        private async Task<string> TestMicrophoneAsync(CancellationToken cancellationToken)
        {
            using var capture = services.CreateAudioCapture();
            var peak = 0f;
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            capture.FramesAvailable += frame =>
            {
                Volatile.Write(ref peak, Math.Max(Volatile.Read(ref peak), frame.Level));
                Dispatcher.BeginInvoke(() => { if (!cancellationToken.IsCancellationRequested) meter.Value = frame.Level; });
            };
            void CaptureStopped(Exception? error) => stopped.TrySetException(error ?? new InvalidOperationException("Le microphone s’est arrêté."));
            capture.CaptureStopped += CaptureStopped;
            try
            {
                var opened = await capture.StartAsync(new AudioCaptureOptions
                {
                    DeviceId = services.Settings.PreferredMicrophoneUID,
                }, cancellationToken);
                var captureTime = Task.Delay(CaptureDuration, cancellationToken);
                var completed = await Task.WhenAny(captureTime, stopped.Task);
                await completed;
                return Volatile.Read(ref peak) > 0.02
                    ? $"Signal détecté · {opened.DeviceName}"
                    : $"Signal trop faible ou absent · {opened.DeviceName}. Vérifiez le niveau d’entrée Windows.";
            }
            finally
            {
                capture.CaptureStopped -= CaptureStopped;
            }
        }

        private async Task<string> TestTranscriptionAsync(CancellationToken cancellationToken)
        {
            var settings = services.Settings;
            var provider = DictationEngineResolver.MakeProvider(settings.Engine, settings, new DictationProviderFactory(services.OfflineSpeech));
            if (provider.UnavailableExplanation is { } reason) throw new InvalidOperationException(reason);
            Render($"Parlez pendant cinq secondes. {settings.Engine.AudioDestinationSentence()}");
            using var manager = new BuddyDictationManager(provider, new DispatcherMainThread(Dispatcher),
                services.CreateAudioCapture, services.MicrophonePermission)
            {
                PreferredMicrophoneId = settings.PreferredMicrophoneUID,
            };
            var transcript = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.AudioPowerLevelChanged += level => meter.Value = level;
            manager.ErrorMessageChanged += error =>
            {
                if (error is not null) transcript.TrySetException(new InvalidOperationException(error));
            };
            manager.SessionActivityChanged += active =>
            {
                // The manager sends any nonempty final text before the next dispatcher turn.
                if (!active) Dispatcher.BeginInvoke(() => transcript.TrySetResult(""));
            };
            await manager.StartPushToTalkFromKeyboardShortcutAsync("", _ => { },
                text => transcript.TrySetResult(text), cancellationToken);
            var captureTime = Task.Delay(CaptureDuration, cancellationToken);
            if (await Task.WhenAny(captureTime, transcript.Task) == captureTime)
            {
                await captureTime;
                Render("Transcription en cours…");
                manager.StopPushToTalkFromKeyboardShortcut();
            }
            var words = await transcript.Task.WaitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(words) ? "Aucun mot reconnu. Réessayez plus près du microphone." : words;
        }

        private void Cancel() => currentTest?.Cancel();
    }
}
