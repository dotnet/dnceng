// Copyright (c) Microsoft. All rights reserved.

namespace ClientApp.Components;

public sealed partial class SupportingContent : ComponentBase
{
    [Parameter, EditorRequired] public string[] DataPoints { get; set; }

    private ParsedSupportingContentItem[] _supportingContent = Array.Empty<ParsedSupportingContentItem>();

    protected override void OnParametersSet()
    {
        if (DataPoints is { Length: > 0 })
        {
            _supportingContent =
                DataPoints.Select(ParseSupportingContent).ToArray();
        }

        base.OnParametersSet();
    }
}
