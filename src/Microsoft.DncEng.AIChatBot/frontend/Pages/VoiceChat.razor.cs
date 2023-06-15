// Copyright (c) Microsoft. All rights reserved.

namespace ClientApp.Pages;

public sealed partial class VoiceChat : ComponentBase, IDisposable
{
    private string _userQuestion = "";
    private ClientApp.Models.UserQuestion _currentQuestion;
    private bool _isRecognizingSpeech = false;
    private bool _isReceivingResponse = false;
    private bool _isReadingResponse = false;
    private IDisposable? _recognitionSubscription;
    private ClientApp.Models.VoicePreferences? _voicePreferences;
    private readonly Dictionary<ClientApp.Models.UserQuestion, string?> _questionAndAnswerMap = new();
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .ConfigureNewLine("\n")
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    [Inject] public OpenAIPromptQueue OpenAIPrompts { get; set; } = null!;
    [Inject] public IDialogService Dialog { get; set; } = null!;
    [Inject] public ISpeechRecognitionService SpeechRecognition { get; set; } = null!;
    [Inject] public ISpeechSynthesisService SpeechSynthesis { get; set; } = null!;
    [Inject] public ILocalStorageService LocalStorage { get; set; } = null!;
    [Inject] public IJSInProcessRuntime JavaScript { get; set; } = null!;
    [Inject] public ILogger<VoiceChat> Logger { get; set; } = null!;

    [CascadingParameter(Name = nameof(IsReversed))]
    public bool IsReversed { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await SpeechRecognition.InitializeModuleAsync();
        }
    }

    private void OnSendPrompt()
    {
        if (_isReceivingResponse || string.IsNullOrWhiteSpace(_userQuestion))
        {
            return;
        }

        _isReceivingResponse = true;
        _currentQuestion = new(_userQuestion, DateTime.Now);
        _questionAndAnswerMap[_currentQuestion] = null;

        OpenAIPrompts.Enqueue(
            _userQuestion,
            async (PromptResponse response) => await InvokeAsync(() =>
            {
                var (_, responseText, isComplete) = response;
                var html = Markdown.ToHtml(responseText, _pipeline);

                _questionAndAnswerMap[_currentQuestion] = html;

                if (isComplete)
                {
                    _voicePreferences = new VoicePreferences(LocalStorage);
                    var (voice, rate, isEnabled) = _voicePreferences;
                    if (isEnabled)
                    {
                        _isReadingResponse = true;
                        var utterance = new SpeechSynthesisUtterance
                        {
                            Rate = rate,
                            Text = responseText
                        };
                        if (voice is not null)
                        {
                            utterance.Voice = new SpeechSynthesisVoice
                            {
                                Name = voice
                            };
                        }
                        SpeechSynthesis.Speak(utterance, duration =>
                        {
                            _isReadingResponse = false;
                            StateHasChanged();
                        });
                    }
                }

                _isReceivingResponse = isComplete is false;
                if (isComplete)
                {
                    _userQuestion = "";
                    _currentQuestion = default;
                }

                StateHasChanged();
            }));
    }

    private void OnKeyUp(KeyboardEventArgs args)
    {
        if (args is { Key: "Enter", ShiftKey: false })
        {
            OnSendPrompt();
        }
    }

    protected override void OnAfterRender(bool firstRender) =>
        JavaScript.InvokeVoid("highlight");

    private void StopTalking()
    {
        SpeechSynthesis.Cancel();
        _isReadingResponse = false;
    }

    private void OnRecognizeSpeechClick()
    {
        if (_isRecognizingSpeech)
        {
            SpeechRecognition.CancelSpeechRecognition(false);
        }
        else
        {
            var bcp47Tag = CultureInfo.CurrentUICulture.Name;

            _recognitionSubscription?.Dispose();
            _recognitionSubscription = SpeechRecognition.RecognizeSpeech(
                bcp47Tag,
                OnRecognized,
                OnError,
                OnStarted,
                OnEnded);
        }
    }

    private async Task ShowVoiceDialogAsync()
    {
        var dialog = await Dialog.ShowAsync<VoiceDialog>(title: "🔊 Text-to-speech Preferences");
        var result = await dialog.Result;
        if (result is not { Canceled: true })
        {
            _voicePreferences = await dialog.GetReturnValueAsync<VoicePreferences>();
        }
    }

    private void OnStarted()
    {
        _isRecognizingSpeech = true;
        StateHasChanged();
    }

    private void OnEnded()
    {
        _isRecognizingSpeech = false;
        StateHasChanged();
    }

    private void OnError(SpeechRecognitionErrorEvent errorEvent)
    {
        Logger.LogWarning(
            "{Error}: {Message}", errorEvent.Error, errorEvent.Message);

        StateHasChanged();
    }

    private void OnRecognized(string transcript)
    {
        _userQuestion = _userQuestion switch
        {
            null => transcript,
            _ => $"{_userQuestion.Trim()} {transcript}".Trim()
        };

        StateHasChanged();
    }

    public void Dispose() => _recognitionSubscription?.Dispose();
}
