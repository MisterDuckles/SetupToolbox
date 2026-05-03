using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WingetAppDeployer_WinUI.Helpers;

// FileSavePicker / FileOpenPicker voor unpackaged WinUI 3 apps. Pickers eisen
// een window-handle (HWND) via WinRT.Interop.InitializeWithWindow zodat ze als
// modal aan ons venster koppelen — zonder die call krijg je een COMException
// "Invalid window handle". HWND uit App.Window pakken.
internal static class FilePickerHelper
{
    public static async Task<StorageFile?> PickSaveFileAsync(string suggestedFileName, string fileTypeName, string fileExtension)
    {
        if (App.Window == null) return null;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName
        };
        picker.FileTypeChoices.Add(fileTypeName, new[] { fileExtension });

        var hWnd = WindowNative.GetWindowHandle(App.Window);
        InitializeWithWindow.Initialize(picker, hWnd);

        return await picker.PickSaveFileAsync();
    }

    public static async Task<StorageFile?> PickOpenFileAsync(string fileExtension)
    {
        if (App.Window == null) return null;

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(fileExtension);

        var hWnd = WindowNative.GetWindowHandle(App.Window);
        InitializeWithWindow.Initialize(picker, hWnd);

        return await picker.PickSingleFileAsync();
    }
}
