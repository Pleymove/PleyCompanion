using System.Windows;
using System.Windows.Controls;
using OpenClicky.Core.Configuration;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Speech;
using OpenClicky.Windows.Platform;
using WpfUi = Wpf.Ui.Controls;

namespace OpenClicky.Windows.AppWindow;

internal sealed partial class SettingsRowFactory
{
    private readonly AudioPlayer localPreviewPlayer = new();
    private CancellationTokenSource? localPreview;

    private UIElement? VoiceModelRow(SettingsItem item, string query)
    {
        if (!item.Id.StartsWith("voiceModel.", StringComparison.Ordinal)) return null;
        if (item.Id == "voiceModel.stt.test") return Ui.Card(new VoiceDiagnosticsRow(item, query, services, transcriptionOnly: true));
        if (services.VoiceModels is not { } runtime)
            return Ui.Card(Ui.Row(item.Title, "Moteur indisponible dans ce contexte.", query, null));
        if (item.Id == "voiceModel.stt.moonshine")
            return new LiveContent(() => MoonshineCard(runtime, item, query), Changes.Shell,
                Changes.Of(Settings, nameof(DictationSettings.Engine), nameof(DictationSettings.LanguageCode)));

        var id = item.Id[(item.Id.LastIndexOf('.') + 1)..];
        if (VoiceModelCatalog.Recognition.FirstOrDefault(model => model.Info.Id == id) is { } stt
            && runtime.FindRecognizer(stt) is { } recognizer)
            return new LiveContent(() => RecognizerCard(runtime, stt, recognizer, item, query), Changes.Of(recognizer), Changes.Shell,
                Changes.Of(Settings, nameof(DictationSettings.Engine), nameof(DictationSettings.LanguageCode)));

        if (VoiceModelCatalog.Synthesis.FirstOrDefault(model => model.Info.Id == id) is { } tts
            && runtime.FindVoice(tts.Choice) is { } voice)
            return new LiveContent(() => SynthesisCard(runtime, voice, item, query), Changes.Of(voice), Changes.Shell);

        return null;
    }

    private UIElement MoonshineCard(VoiceModelRuntime runtime, SettingsItem item, string query)
    {
        var model = VoiceModelCatalog.Moonshine;
        var installed = MoonshineFacts.IsInstalled;
        var active = ActiveRecognitionRoles(model.Id, DictationEngineChoice.Moonshine, MoonshineFacts.Hears, installed);
        var canUse = MoonshineFacts.Hears(Settings.Language.BareCode) && MoonshineFacts.Hears(ReplyLanguage.CurrentCode);
        var activate = installed && canUse && !active.Both
            ? Ui.Button("Utiliser", () => SetRecognition(() => runtime.SelectMoonshine(Settings)), WpfUi.ControlAppearance.Primary)
            : null;
        var detail = installed && !canUse
            ? "Sélectionnez l’anglais pour l’assistant et l’anglais ou la détection automatique pour la dictée."
            : null;
        return VoiceCard(item, model, query, installed ? active.Label : "Indisponible dans ce build",
            active.Any, detail, activate);
    }

    private UIElement RecognizerCard(VoiceModelRuntime runtime, LocalSttModel model, ParakeetDictation recognizer,
        SettingsItem item, string query)
    {
        var selected = ParakeetDictation.SelectedModel == model;
        var installed = recognizer.IsModelInstalled;
        var active = ActiveRecognitionRoles(model.Info.Id, selected ? DictationEngineChoice.Parakeet : null, ParakeetFacts.Hears, installed);
        var canUse = ParakeetFacts.Hears(Settings.Language.BareCode) && ParakeetFacts.Hears(ReplyLanguage.CurrentCode);
        var activate = installed && canUse && !active.Both
            ? Ui.Button("Utiliser", () => SetRecognition(() => runtime.SelectRecognizer(model, Settings)), WpfUi.ControlAppearance.Primary)
            : null;
        var detail = installed && !canUse ? "Les langues de l’assistant et de la dictée doivent être prises en charge." : null;
        return DownloadCard(recognizer, item, query, active.Label, active.Any, activate, detail,
            () => SetRecognition(() => _ = runtime.RemoveRecognizerAsync(model)));
    }

    private (string Label, bool Any, bool Both) ActiveRecognitionRoles(string id, DictationEngineChoice? engine,
        Func<string?, bool> hears, bool installed)
    {
        var dictation = installed && Settings.Engine == engine && hears(Settings.Language.BareCode);
        var assistant = installed && services.AssistantLocalSttId() == id;
        return (dictation, assistant) switch
        {
            (true, true) => ("Actif · dictée et assistant", true, true),
            (true, false) => ("Actif · dictée", true, false),
            (false, true) => ("Actif · assistant", true, false),
            _ => ("Installé", false, false),
        };
    }

    private void SetRecognition(Action select)
    {
        if (services.IsVoiceInputBusy())
        {
            MessageBox.Show(actions.OwnerWindow, "Arrêtez la dictée ou la conversation vocale avant de changer de modèle.",
                "Reconnaissance vocale", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        select();
        ShellSettingsRevision.NoteChanged();
    }

    private UIElement SynthesisCard(VoiceModelRuntime runtime, LocalSpeechVoice voice, SettingsItem item, string query)
    {
        var model = voice.Model;
        var language = ReplyLanguage.CurrentCode;
        var installed = voice.IsModelInstalled;
        var active = installed && SpeechVoiceChoices.Current() == model.Choice;
        var canUse = model.SpeakerFor(language, null) is not null;
        var activate = installed && canUse && OpenClickyConfiguration.Settings.SpeechVoice != model.Choice.StoredValue()
            ? Ui.Button("Utiliser", () =>
            {
                runtime.SelectVoice(model);
                ShellSettingsRevision.NoteChanged();
            }, WpfUi.ControlAppearance.Primary)
            : null;
        var detail = installed && !canUse ? "Pour activer cette voix, sélectionnez une langue prise en charge dans Clicky." : null;
        var card = new StackPanel();
        card.Children.Add(DownloadCard(voice, item, query, active ? "Actif · assistant" : "Installé", active, activate, detail));
        if (installed && !voice.State.IsBusy) card.Children.Add(VoicePreview(voice, canUse ? language : "fr"));
        return card;
    }

    private UIElement VoicePreview(LocalSpeechVoice voice, string language)
    {
        var message = Ui.Text("", SettingsLook.CaptionSize, SettingsLook.TextSecondary);
        message.Visibility = Visibility.Collapsed;
        message.Margin = new Thickness(0, 8, 0, 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(message, System.Windows.Automation.AutomationLiveSetting.Polite);
        CancellationTokenSource? here = null;
        Button? listen = null;

        async Task PreviewAsync()
        {
            if (here is not null)
            {
                here.Cancel();
                return;
            }
            localPreview?.Cancel();
            using var preview = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            here = localPreview = preview;
            listen!.Content = "Arrêter";
            message.Text = "Préparation de l’aperçu…";
            message.Visibility = Visibility.Visible;
            try
            {
                var audio = await voice.AudioAsync(ReplyLanguage.PreviewSentence(language), language, preview.Token);
                message.Visibility = Visibility.Collapsed;
                await localPreviewPlayer.PlayToCompletionAsync(audio, preview.Token);
            }
            catch (OperationCanceledException) when (preview.IsCancellationRequested)
            {
                message.Visibility = Visibility.Collapsed;
            }
            catch (Exception error)
            {
                AppLog.Write($"voice preview ({voice.Info.Id}): {error.Message}");
                message.Text = "Impossible de lire cet aperçu. " + error.Message;
                message.Visibility = Visibility.Visible;
            }
            finally
            {
                if (localPreview == preview) localPreview = null;
                here = null;
                listen.Content = "Écouter";
                if (SpeechVoiceChoices.Current() != voice.Model.Choice) _ = voice.UnloadAsync();
            }
        }

        listen = Ui.Button("Écouter", () => _ = PreviewAsync());
        UIElement? speakers = null;
        if (voice.Model.Choice == SpeechVoiceChoice.Kokoro)
        {
            var options = services.Services.KokoroSpeakers(language);
            if (options.Count > 1)
            {
                speakers = Ui.Picker(options.Select(option => (option.Id, option.Title)),
                    services.Services.CurrentKokoroSpeaker(language) ?? "", id =>
                    {
                        if (int.TryParse(id, out var speaker))
                            OpenClickyConfiguration.Update(shell => shell.KokoroSpeaker = speaker);
                    }, 160);
            }
        }
        var body = new StackPanel();
        body.Children.Add(Ui.Row("Aperçu", language == "fr" ? "Extrait en français" : $"Extrait · {ReplyLanguage.EnglishName(language)}", "",
            Ui.HStack(8, speakers, listen)));
        body.Children.Add(message);
        body.Unloaded += (_, _) => here?.Cancel();
        body.IsVisibleChanged += (_, _) => { if (!body.IsVisible) here?.Cancel(); };
        return Ui.Card(body);
    }

    private static UIElement DownloadCard(DownloadableVoiceModel model, SettingsItem item, string query,
        string installedLabel, bool active, UIElement? activate, string? detail, Action? remove = null)
    {
        var installed = model.IsModelInstalled;
        var status = installed ? installedLabel : "À télécharger";
        UIElement? primary = installed ? activate : Ui.Button("Télécharger", model.Download, WpfUi.ControlAppearance.Primary);
        UIElement? secondary = installed ? Ui.Button("Supprimer", remove ?? (() => _ = model.RemoveAsync())) : null;
        ProgressBar? progress = null;
        switch (model.State)
        {
            case ModelInstallState.DownloadingState download:
                status = $"Téléchargement · {download.Fraction:P0}";
                progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = download.Fraction, Height = 5 };
                primary = Ui.Button("Annuler", model.CancelDownload);
                break;
            case ModelInstallState.UnpackingState:
                status = "Vérification et extraction";
                progress = new ProgressBar { IsIndeterminate = true, Height = 5 };
                primary = Ui.Button("Annuler", model.CancelDownload);
                break;
            case ModelInstallState.RemovingState:
                status = "Suppression…";
                primary = null;
                break;
            case ModelInstallState.FailedState failure:
                status = installed ? installedLabel : "Échec";
                detail = failure.Message;
                if (!installed) primary = Ui.Button("Réessayer", model.Download, WpfUi.ControlAppearance.Primary);
                break;
        }

        return VoiceCard(item, model.Info, query, status, active, detail, primary, secondary, progress);
    }

    private static UIElement VoiceCard(SettingsItem item, VoiceModelInfo model, string query, string status, bool active,
        string? detail, UIElement? primary, UIElement? secondary = null, ProgressBar? progress = null)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Row(item.Title, $"{item.Detail} · {model.Size}", query, Ui.Tag(status, active)));
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var note = Ui.Text(detail, SettingsLook.CaptionSize, SettingsLook.TextSecondary);
            note.Margin = new Thickness(0, 7, 0, 0);
            body.Children.Add(note);
        }
        if (progress is not null)
        {
            progress.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(progress);
        }
        if (primary is not null || secondary is not null)
        {
            var actions = Ui.HStack(8, primary, secondary);
            actions.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(actions);
        }
        return Ui.Card(body);
    }
}
