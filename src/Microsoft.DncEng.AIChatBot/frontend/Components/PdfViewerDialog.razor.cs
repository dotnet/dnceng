// Copyright (c) Microsoft. All rights reserved.

namespace ClientApp.Components;

public sealed partial class PdfViewerDialog
{
    private bool _isLoading = true;
    private string _pdfViewerVisibilityStyle => _isLoading ? "display:none;" : "display:default;";

    [Parameter] public string FileName { get; set; }
    [Parameter] public string BaseUrl { get; set; }

    [CascadingParameter] public MudDialogInstance Dialog { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // TODO
        //await JavaScriptModule.RegisterIFrameLoadedAsync(
        //    "#pdf-viewer",
        //    () =>
        //    {
        //        _isLoading = false;
        //        StateHasChanged();
        //    });
    }

    private void OnCloseClick() => Dialog.Close(DialogResult.Ok(true));
}
