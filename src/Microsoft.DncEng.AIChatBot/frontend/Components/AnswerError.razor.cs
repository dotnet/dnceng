// Copyright (c) Microsoft. All rights reserved.

using M = global::Shared.Models;

namespace ClientApp.Components;

public sealed partial class AnswerError
{
    [Parameter, EditorRequired] public string Question { get; set; }
    [Parameter, EditorRequired] public M.ApproachResponse Error { get; set; }
    [Parameter, EditorRequired] public EventCallback<string> OnRetryClicked { get; set; }

    private async Task OnRetryClickedAsync()
    {
        if (OnRetryClicked.HasDelegate)
        {
            await OnRetryClicked.InvokeAsync(Question);
        }
    }
}
