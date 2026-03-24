Place the extracted Fixed Version WebView2 Runtime files in a folder named WebView2Runtime next to DatabaseViewer.App.csproj.

Expected publish layout:

- DatabaseViewer.App.exe
- WebView2Loader.dll
- WebView2Runtime\msedgewebview2.exe
- WebView2Runtime\...other fixed runtime files...

How to prepare it:

1. Download the Fixed Version WebView2 Runtime matching your publish architecture, for example x64.
2. Extract it into DatabaseViewer.App/WebView2Runtime/.
3. Publish the app normally. The csproj is configured to copy WebView2Runtime/** into the publish output.

If WebView2Runtime is absent, the app falls back to the machine-installed Evergreen WebView2 Runtime.