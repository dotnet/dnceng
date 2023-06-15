// Copyright (c) Microsoft. All rights reserved.

using ClientApp.Models;

namespace ClientApp.Components;

public sealed partial class VoiceDialog : ComponentBase, IDisposable
{
    private SpeechSynthesisVoice[] _voices = Array.Empty<SpeechSynthesisVoice>();
    private readonly IList<double> _voiceSpeeds =
        Enumerable.Range(0, 12).Select(i => (i + 1) * .25).ToList();
    private ClientApp.Models.VoicePreferences? _voicePreferences;
    private RequestVoiceState _state;

    [Inject] public ISpeechSynthesisService SpeechSynthesis { get; set; } = null!;

    [Inject] public ILocalStorageService LocalStorage { get; set; } = null!;

    [CascadingParameter] public MudDialogInstance Dialog { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _state = RequestVoiceState.RequestingVoices;

        await GetVoicesAsync();
        SpeechSynthesis.OnVoicesChanged(() => GetVoicesAsync(true));

        _voicePreferences = new VoicePreferences(LocalStorage);

        if (_voicePreferences.Voice is null &&
            _voices.FirstOrDefault(voice => voice.Default) is { } voice)
        {
            _voicePreferences.Voice = voice.Name;
        }
    }

    private async Task GetVoicesAsync(bool isFromCallback = false)
    {
        _voices = await SpeechSynthesis.GetVoicesAsync();
        if (_voices is { } && isFromCallback)
        {
            StateHasChanged();
        }

        if (_voices is { Length: > 0 })
        {
            _state = RequestVoiceState.FoundVoices;
        }
    }

    private void OnValueChanged(string selectedVoice) => _voicePreferences = _voicePreferences! with
    {
        Voice = selectedVoice
    };

    private void OnSaveVoiceSelection() => Dialog.Close(DialogResult.Ok(_voicePreferences));

    private void OnCancel() => Dialog.Close(DialogResult.Ok(_voicePreferences));

    public void Dispose() => SpeechSynthesis.UnsubscribeFromVoicesChanged();
}

internal enum RequestVoiceState
{
    RequestingVoices,
    FoundVoices,
    Error
};
