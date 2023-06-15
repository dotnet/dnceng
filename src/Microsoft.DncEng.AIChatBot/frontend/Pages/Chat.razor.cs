// Copyright (c) Microsoft. All rights reserved.

using M = global::Shared.Models;

namespace ClientApp.Pages;

public sealed partial class Chat
{
    private string _userQuestion = "";
    private ClientApp.Models.UserQuestion _currentQuestion;
    private string _lastReferenceQuestion = "";
    private bool _isReceivingResponse = false;

    private readonly Dictionary<ClientApp.Models.UserQuestion, M.ApproachResponse?> _questionAndAnswerMap = new();

    [Inject] public ISessionStorageService SessionStorage { get; set; } = null!;

    [Inject] public ApiClient ApiClient { get; set; } = null!;

    [CascadingParameter(Name = nameof(Settings))]
    public ClientApp.Models.RequestSettingsOverrides Settings { get; set; }

    [CascadingParameter(Name = nameof(IsReversed))]
    public bool IsReversed { get; set; }

    private Task OnAskQuestionAsync(string question)
    {
        _userQuestion = question;
        return OnAskClickedAsync();
    }

    private async Task OnAskClickedAsync()
    {
        if (string.IsNullOrWhiteSpace(_userQuestion))
        {
            return;
        }

        _isReceivingResponse = true;
        _lastReferenceQuestion = _userQuestion;
        _currentQuestion = new(_userQuestion, DateTime.Now);
        _questionAndAnswerMap[_currentQuestion] = null;

        try
        {
            var history = _questionAndAnswerMap
                .Where(x => x.Value is not null)
                .Select(x => new ChatTurn(x.Key.Question, x.Value!.Answer))
                .ToList();

            history.Add(new ChatTurn(_userQuestion));

            var request = new ChatRequest(history.ToArray(), Settings.Approach, Settings.Overrides);
            var result = await ApiClient.ChatConversationAsync(request);

            _questionAndAnswerMap[_currentQuestion] = result.Response;
            if (result.IsSuccessful)
            {
                _userQuestion = "";
                _currentQuestion = default;
            }
        }
        finally
        {
            _isReceivingResponse = false;
        }
    }

    private void OnClearChat()
    {
        _userQuestion = _lastReferenceQuestion = "";
        _currentQuestion = default;
        _questionAndAnswerMap.Clear();
    }
}
