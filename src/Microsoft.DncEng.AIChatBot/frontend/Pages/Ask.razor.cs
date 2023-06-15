﻿// Copyright (c) Microsoft. All rights reserved.

using M = global::Shared.Models;

namespace ClientApp.Pages;

public sealed partial class Ask
{
    private string _userQuestion = "";
    private string _lastReferenceQuestion = "";
    private bool _isReceivingResponse = false;
    private M.ApproachResponse? _approachResponse = null;

    [Inject] public ISessionStorageService SessionStorage { get; set; } = null!;
    [Inject] public ApiClient ApiClient { get; set; } = null!;

    [CascadingParameter(Name = nameof(Settings))]
    public ClientApp.Models.RequestSettingsOverrides Settings { get; set; }

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

        try
        {
            var request = new M.AskRequest(
                Question: _userQuestion,
                Approach: Settings.Approach,
                Overrides: Settings.Overrides);

            var result = await ApiClient.AskQuestionAsync(request);
            _approachResponse = result.Response;
        }
        finally
        {
            _isReceivingResponse = false;
        }
    }

    private void OnClearChat()
    {
        _userQuestion = _lastReferenceQuestion = "";
        _approachResponse = null;
    }
}
