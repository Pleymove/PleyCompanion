// Ports Dictation/UI/SettingsClickyPage.swift: Settings → Clicky's rows. Who answers and the Claude
// model, when the pointer shows (one picker over two settings, PointerShowsChoice), agent mode and its
// workspace, and the voice: the language Clicky speaks and listens in, who speaks the answers with
// this PC's own rows, fast voice, hands-free and who runs them (Voice was a page of its
// own; SettingsLayout now puts it on Clicky). The voice picker's rules come from ISettingsServices (SpeechVoiceChoice); this PC's
// voice is Windows' own (ISystemVoice), the Mac voice's twin.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Pointing;
using OpenClicky.Core.Providers;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shortcuts;
using OpenClicky.Core.Speech;

namespace OpenClicky.Windows.AppWindow;

internal sealed partial class SettingsRowFactory
{
    private UIElement? ClickyRow(SettingsItem item, string query)
    {
        var companion = services.Companion;
        switch (item.Id)
        {
            case "clicky.pointerColor":
                return Ui.Card(Ui.Row(item.Title, item.Detail, query,
                    Ui.Picker(PointerLook.Colors.Select(color => (color.Name, color.Title)),
                        services.Preferences.GetString(PointerLook.ColorPreferenceKey) ?? PointerLook.DefaultColor,
                        name => services.Preferences.Set(PointerLook.ColorPreferenceKey, name), 160)));
            case "clicky.pointerStyle":
                return Ui.Card(Ui.Row(item.Title, item.Detail, query,
                    Ui.Picker(PointerLook.Styles.Select(style => (style, style.Title())),
                        PointerLook.StyleFrom(services.Preferences.GetString(PointerLook.StylePreferenceKey)),
                        style => services.Preferences.Set(PointerLook.StylePreferenceKey, style.RawValue()), 160)));
            case "keyboard.mouseTalk":
            {
                var picked = MouseTalkButtons.FromRawValue(services.Preferences.GetString(MouseTalkButtons.PreferenceKey));
                return Ui.Card(Ui.Row(item.Title, item.Detail, query,
                    Ui.Picker(MouseTalkButtons.AllCases.Select(button => (button, button.Title())), picked,
                        button => services.Preferences.Set(MouseTalkButtons.PreferenceKey, button.RawValue()), 200)));
            }
            case "clicky.assistantName":
                return Ui.Card(AssistantNameRow(item, query));
            case "clicky.answerSource":
                return new LiveContent(() => Ui.Card(AnswerSourceRow(item, query)), Changes.Shell);
            case "clicky.model":
                return new LiveContent(() => Ui.Card(Ui.Row(item.Title, item.Detail, query,
                    Ui.Picker(new[] { ("claude-sonnet-4-6", "Sonnet"), ("claude-opus-4-6", "Opus") }, companion.SelectedModel, companion.SetSelectedModel, 130))),
                    Changes.Of(companion, nameof(ICompanionSettings.SelectedModel)));
            case "clicky.pointer":
                // One picker over two settings: off is "while I talk"; on, the presence picks the rest.
                return new LiveContent(() =>
                {
                    var choice = PointerShowsChoices.From(companion.IsClickyCursorEnabled, companion.PointerPresence);
                    return Ui.Card(Ui.Row(item.Title, choice.Detail(), query,
                        Ui.Picker(PointerShowsChoices.AllCases.Select(option => (option, option.Title())), choice, newChoice =>
                        {
                            companion.SetClickyCursorEnabled(newChoice != PointerShowsChoice.WhileTalking);
                            if (newChoice.Presence() is { } presence) companion.SetPointerPresence(presence);
                        }, 200)));
                }, Changes.Of(companion, nameof(ICompanionSettings.PointerPresence), nameof(ICompanionSettings.IsClickyCursorEnabled)));

            case "clicky.agentMode":
                return CompanionToggle(item, query, () => companion.IsAgentModeEnabled, companion.SetAgentModeEnabled, nameof(ICompanionSettings.IsAgentModeEnabled));
            case "clicky.workspace":
                return new LiveContent(() => Ui.Card(Ui.Row(item.Title, SettingsCatalog.DisplayPath(OpenClickyConfiguration.WorkspacePath), query,
                    Ui.HStack(8, Ui.Button("Show in Explorer", () =>
                    {
                        Directory.CreateDirectory(OpenClickyConfiguration.WorkspacePath);
                        Ui.RevealInExplorer(OpenClickyConfiguration.WorkspacePath);
                    }), Ui.Button("Change…", ChooseWorkspaceFolder)))), Changes.Shell);
            default:
                return null;
        }
    }

    private LiveContent CompanionToggle(SettingsItem item, string query, Func<bool> read, Action<bool> write, string propertyName) =>
        new(() => Ui.Card(Ui.Row(item.Title, item.Detail, query, Ui.Toggle(read(), write))), Changes.Of(services.Companion, propertyName));

    /// <summary>The assistant's name: saved when the field loses focus or Enter is pressed; blank goes back to OpenClicky.</summary>
    private static Grid AssistantNameRow(SettingsItem item, string query)
    {
        var field = new Wpf.Ui.Controls.TextBox
        {
            Width = 190, Text = AssistantName.Cleaned(OpenClickyConfiguration.Settings.AssistantName) ?? "", PlaceholderText = AssistantName.Default,
            MaxLength = AssistantName.MaximumLength, VerticalAlignment = VerticalAlignment.Center,
        };
        Ui.AutomationProperties(field, "Assistant name");
        void Save()
        {
            var name = AssistantName.Cleaned(field.Text);
            if (name == AssistantName.Cleaned(OpenClickyConfiguration.Settings.AssistantName)) return;
            OpenClickyConfiguration.Update(shell => shell.AssistantName = name);
            ShellSettingsRevision.NoteChanged();
            field.Text = name ?? "";
        }
        field.LostFocus += (_, _) => Save();
        field.KeyDown += (_, arguments) => { if (arguments.Key == System.Windows.Input.Key.Enter) Save(); };
        return Ui.Row(item.Title, item.Detail, query, field);
    }

    /// <summary>Who answers questions: automatic, or one source that is set up. Shows who is answering now.</summary>
    private static Grid AnswerSourceRow(SettingsItem item, string query)
    {
        var credentials = ProviderCredentials.Current();
        var available = credentials.AnswerSourcesInOrder;
        var picked = AnswerSources.FromRawValue(OpenClickyConfiguration.Settings.AnswerSource);
        var options = new List<(string Id, string Title)> { ("auto", "Automatic") };
        options.AddRange(available.Select(source => (source.Id(), source.DisplayName())));
        // A pick that is no longer set up stays visible, so the menu does not jump.
        if (picked is { } stale && !available.Contains(stale)) options.Add((stale.Id(), $"{stale.DisplayName()} (not set up)"));
        var picker = Ui.Picker(options, picked?.Id() ?? "auto", choice =>
        {
            OpenClickyConfiguration.Update(shell => shell.AnswerSource = choice == "auto" ? null : choice);
            ShellSettingsRevision.NoteChanged();
        }, 220);
        return Ui.Row(item.Title, $"Now: {AccountRows.NowAnswering(credentials)}", query, picker);
    }

    private void ChooseWorkspaceFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the agents' workspace",
            InitialDirectory = Directory.Exists(OpenClickyConfiguration.WorkspacePath) ? OpenClickyConfiguration.WorkspacePath : OpenClickyPaths.HomeDirectory,
        };
        if (dialog.ShowDialog(actions.OwnerWindow) != true) return;
        OpenClickyConfiguration.Update(shell => shell.Workspace = dialog.FolderName);
        ShellSettingsRevision.NoteChanged();
    }

    // MARK: - Voice

    private UIElement? VoiceRow(SettingsItem item, string query)
    {
        var companion = services.Companion;
        switch (item.Id)
        {
            case "clicky.language":
                return new LiveContent(() =>
                {
                    var currentCode = ReplyLanguage.CurrentCode;
                    return Ui.Card(Ui.Row(item.Title, "Clicky speaks and listens in this. Dictation has its own, on the Dictation page.", query,
                        Ui.Picker(ReplyLanguage.ChoicesFor(Settings.Region, currentCode).Select(choice => (choice.Code, choice.Name)), currentCode, code =>
                        {
                            OpenClickyConfiguration.Update(shell => shell.Language = code);
                            ShellSettingsRevision.NoteChanged();
                        }, 150)));
                }, Changes.Shell, Changes.Of(Settings, nameof(Core.Dictation.DictationSettings.Region)));
            case "clicky.transcriptionProvider":
                return new LiveContent(() =>
                {
                    var current = BuddyTranscriptionProviderFactory.ParsePreferredProvider(OpenClickyConfiguration.Settings.TranscriptionProvider);
                    var selected = current switch
                    {
                        BuddyTranscriptionProviderFactory.PreferredProvider.Parakeet => "parakeet",
                        BuddyTranscriptionProviderFactory.PreferredProvider.Moonshine => "moonshine",
                        BuddyTranscriptionProviderFactory.PreferredProvider.OfflineSpeech => "offline",
                        BuddyTranscriptionProviderFactory.PreferredProvider.OpenAI => "openai",
                        BuddyTranscriptionProviderFactory.PreferredProvider.AssemblyAI => "assemblyai",
                        _ => "auto",
                    };
                    var providers = new List<(string, string)>
                    {
                        ("auto", "Automatique"), ("parakeet", ParakeetDictation.SelectedModel.Info.Name),
                        ("offline", "Moteurs du PC"), ("openai", "OpenAI (en ligne)"), ("assemblyai", "AssemblyAI (en ligne)"),
                    };
                    if (MoonshineFacts.IsInstalled || selected == "moonshine") providers.Insert(2, ("moonshine", "Moonshine Tiny"));
                    var picker = Ui.Picker(providers, selected, choice =>
                    {
                        OpenClickyConfiguration.Update(shell => shell.TranscriptionProvider = choice == "auto" ? null : choice);
                        ShellSettingsRevision.NoteChanged();
                    }, 170);
                    return Ui.Card(Ui.Row(item.Title, item.Detail, query,
                        Ui.HStack(8, picker, Ui.Button("Modèles locaux", () => actions.ShowSettings(DictationSettingsPage.Recognition)))));
                }, Changes.Shell);
            case "clicky.speechVoice":
                var voices = (services.VoiceModels?.Models ?? []).OfType<LocalSpeechVoice>().Select(model => Changes.Of(model));
                return new LiveContent(() => SpeechVoiceRows(item, query),
                    [Changes.Shell, Changes.Of(Settings, nameof(Core.Dictation.DictationSettings.Region)), .. voices]);
            case "clicky.emailApp":
                // emailApp in shell.json: automatic saves nothing; anything unknown (the Mac's "mail") shows as automatic.
                return new LiveContent(() =>
                {
                    var picked = Core.Actions.EmailApps.Named(OpenClickyConfiguration.Settings.EmailApp)?.RawValue() ?? "auto";
                    return Ui.Card(Ui.Row(item.Title, item.Detail, query,
                        Ui.Picker(new[] { ("auto", "Automatic"), ("gmail", "Gmail"), ("outlook", "Outlook") }, picked, choice =>
                        {
                            OpenClickyConfiguration.Update(shell => shell.EmailApp = choice == "auto" ? null : choice);
                            ShellSettingsRevision.NoteChanged();
                        }, 150)));
                }, Changes.Shell);
            case "clicky.fastVoice":
                return CompanionToggle(item, query, () => companion.IsRealtimeVoiceEnabled, companion.SetRealtimeVoiceEnabled, nameof(ICompanionSettings.IsRealtimeVoiceEnabled));
            case "clicky.alwaysListening":
                return CompanionToggle(item, query, () => companion.IsAlwaysListening, companion.SetAlwaysListening, nameof(ICompanionSettings.IsAlwaysListening));
            case "clicky.realtimeProvider":
                return new LiveContent(() => Ui.Card(Ui.Row(item.Title, item.Detail, query,
                    Ui.Picker(SettingsCatalog.RealtimeProviderChoices, SettingsCatalog.RealtimeProviderChoice(OpenClickyConfiguration.Settings.RealtimeProvider), choice =>
                    {
                        OpenClickyConfiguration.Update(shell => shell.RealtimeProvider = choice == "auto" ? null : choice);
                        ShellSettingsRevision.NoteChanged();
                        companion.RealtimeProviderChanged();
                    }, 170))), Changes.Shell);
            case "clicky.fastVoiceNeedsKey":
                return Ui.Card(Ui.Row(item.Title, item.Detail, query, Ui.Button("Add Key…", () => actions.ShowSettings(DictationSettingsPage.Account))));
            default:
                return null;
        }
    }

    /// <summary>
    /// Who speaks the answers: Automatic, every voice there is a key or an account for, Kokoro and
    /// this PC; the line under it names who speaks now. Kokoro and this PC's voice each get their own
    /// rows under it, and "More voices" points at Keys & Account.
    /// </summary>
    private StackPanel SpeechVoiceRows(SettingsItem item, string query)
    {
        var voices = services.Services;
        var picker = voices.VoicePicker(Settings.Region);
        var stack = new StackPanel();
        stack.Children.Add(Ui.Card(Ui.Row(item.Title, $"Now: {picker.NowSpeakingSentence}", query,
            Ui.Picker(picker.Offered.Select(option => (option.Id, option.Title)), picker.PickedId, voiceId =>
            {
                voices.PickVoice(voiceId);
                ShellSettingsRevision.NoteChanged();
            }, 200))));
        stack.Children.Add(Ui.Card(Ui.Row("Modèles de synthèse locale", "Téléchargez un modèle et écoutez un extrait.", query,
            Ui.Button("Gérer", () => actions.ShowSettings(DictationSettingsPage.Synthesis)))));
        if (!string.IsNullOrWhiteSpace(OpenClickyConfiguration.Settings.GeminiApiKey)) stack.Children.Add(GeminiVoiceRow());
        if (picker.ShowsSystemVoiceRow) foreach (var row in SystemVoiceRows()) stack.Children.Add(row);
        if (picker.HasMoreVoices)
        {
            var vendors = Settings.Region.OffersIndianOptions()
                ? "OpenAI, ElevenLabs, Gemini, Google Cloud, Amazon Polly or Sarvam"
                : "OpenAI, ElevenLabs, Gemini, Google Cloud or Amazon Polly";
            stack.Children.Add(Ui.Card(Ui.Row("More voices", $"Add an {vendors} key under Keys & Account, and its voice shows up here.", query,
                Ui.Button("Add a Key…", () => actions.ShowSettings(DictationSettingsPage.Account)))));
        }
        return stack;
    }

    /// <summary>Gemini's voice (with a Gemini key): which of its voices speaks, and a preview of it in the language Clicky speaks.
    /// The same voice speaks for the fast voice on Gemini.</summary>
    private UIElement GeminiVoiceRow()
    {
        var shell = OpenClickyConfiguration.Settings;
        var current = CloudVoiceRequests.GeminiVoiceFor(shell);
        var picker = Ui.Picker(CloudVoiceRequests.GeminiVoices.Select(voice => (voice.Name, $"{voice.Name} · {voice.Trait}")), current, name =>
        {
            OpenClickyConfiguration.Update(saved => saved.GeminiVoice = name);
            ShellSettingsRevision.NoteChanged();
            _ = services.Services.PreviewVoiceAsync("gemini");
        }, 200, maxWidth: 240);
        var language = ReplyLanguage.EnglishName(ReplyLanguage.CurrentCode);
        var detail = $"Gemini speaks {language} natively, without an English accent. Picking one says a line in it; the fast voice on Gemini uses it too.";
        if (ReplyLanguage.CurrentCode != "en" && SpeechVoiceChoices.FromStored(shell.SpeechVoice) != SpeechVoiceChoice.Gemini)
        {
            detail += $" Pick “{SpeechVoiceChoice.Gemini.Title()}” above for {language} answers in it.";
        }
        return Ui.Card(Ui.Row("Gemini voice", detail, "", Ui.HStack(8, picker, Ui.Button("Preview", () => _ = services.Services.PreviewVoiceAsync("gemini")))));
    }

    /// <summary>This PC's voice: which one, a preview, and where Windows downloads the better ones.</summary>
    private IEnumerable<UIElement> SystemVoiceRows()
    {
        var systemVoice = services.SystemVoice;
        var language = ReplyLanguage.CurrentCode;
        IReadOnlyList<Core.Platform.SystemVoiceInfo> installed;
        try { installed = systemVoice.Voices(language); }
        catch (Exception error)
        {
            AppLog.Write($"voice: couldn't list this PC's voices: {error.Message}");
            installed = [];
        }
        var pickedId = OpenClickyConfiguration.Settings.MacVoiceIdentifier;
        var current = installed.Count == 0 ? null : systemVoice.CurrentVoice(language, pickedId);
        var picker = Ui.Picker(installed.Select(voice => (voice.Id, voice.Name)), current?.Id ?? "", voiceId =>
        {
            OpenClickyConfiguration.Update(shell => shell.MacVoiceIdentifier = voiceId);
            ShellSettingsRevision.NoteChanged();
            _ = PreviewSystemVoiceAsync(voiceId);
        }, 200, maxWidth: 240);
        yield return Ui.Card(Ui.Row("This PC’s voice", installed.Count == 0 ? "No voice for this language is installed yet." : "Picking one says a line in it.", "",
            Ui.HStack(8, picker, Ui.Button("Preview", () => _ = PreviewSystemVoiceAsync(OpenClickyConfiguration.Settings.MacVoiceIdentifier)))));
        yield return Ui.Card(Ui.Row("Better voices",
            "Windows downloads more natural voices once, then they run offline. Get Voices opens Settings › Time & language › Speech.", "",
            Ui.Button("Get Voices…", () => Ui.OpenExternal("ms-settings:speech"))));
    }

    private async Task PreviewSystemVoiceAsync(string? voiceId)
    {
        try
        {
            var wav = await services.SystemVoice.RenderWavAsync(ReplyLanguage.PreviewSentence(ReplyLanguage.CurrentCode), ReplyLanguage.CurrentCode, voiceId);
            await services.AudioPlayer.PlayToCompletionAsync(wav);
        }
        catch (Exception error)
        {
            AppLog.Write($"voice: preview failed: {error.Message}");
        }
    }
}
