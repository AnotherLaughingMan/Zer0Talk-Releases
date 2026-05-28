using System.Text.Json;
using Xunit;
using Zer0Talk.Services;

namespace Zer0Talk.Tests;

public sealed class MarkdownIpcEndpointServiceTests
{
    [Fact]
    public void TryHandleRequest_RenderMarkdown_ReturnsHtmlPayload()
    {
        var endpoint = new MarkdownIpcEndpointService();
        using var request = JsonDocument.Parse("""
        {
          "type": "request",
          "id": "r1",
          "command": "markdown.render.get",
          "payload": {
            "markdown": "# Header"
          }
        }
        """);

        var handled = endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandRender, request.RootElement, out var responseJson);

        Assert.True(handled);
        Assert.False(string.IsNullOrWhiteSpace(responseJson));

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(MarkdownIpcEndpointService.RenderSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("# Header", root.GetProperty("markdown").GetString());
        Assert.Contains("<h1", root.GetProperty("html").GetString() ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryHandleRequest_UnknownCommand_ReturnsFalse()
    {
        var endpoint = new MarkdownIpcEndpointService();
        using var request = JsonDocument.Parse("{" + "\"payload\":{\"markdown\":\"test\"}" + "}");

        var handled = endpoint.TryHandleRequest("markdown.unknown", request.RootElement, out var responseJson);

        Assert.False(handled);
        Assert.Equal(string.Empty, responseJson);
    }

    [Fact]
    public void TryHandleRequest_FormatApply_BoldWrapsSelection()
    {
        var endpoint = new MarkdownIpcEndpointService();
        using var request = JsonDocument.Parse("""
        {
          "type": "request",
          "id": "r2",
          "command": "markdown.format.apply",
          "payload": {
            "markdown": "hello world",
            "selectionStart": 0,
            "selectionEnd": 5,
            "kind": "bold"
          }
        }
        """);

        var handled = endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandFormatApply, request.RootElement, out var responseJson);

        Assert.True(handled);
        Assert.False(string.IsNullOrWhiteSpace(responseJson));

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        Assert.Equal(MarkdownIpcEndpointService.FormatSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("**hello** world", root.GetProperty("markdown").GetString());
        Assert.Equal(2, root.GetProperty("selectionStart").GetInt32());
        Assert.Equal(7, root.GetProperty("selectionEnd").GetInt32());
        Assert.Equal("bold", root.GetProperty("kind").GetString());
    }

      [Fact]
      public void TryHandleRequest_UiConfigGet_ReturnsPreviewToolbarAndMiniEditorConfig()
      {
        var endpoint = new MarkdownIpcEndpointService();
        using var request = JsonDocument.Parse("""
        {
          "type": "request",
          "id": "r3",
          "command": "markdown.ui.config.get"
        }
        """);

        var handled = endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandUiConfigGet, request.RootElement, out var responseJson);

        Assert.True(handled);
        Assert.False(string.IsNullOrWhiteSpace(responseJson));

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        Assert.Equal(MarkdownIpcEndpointService.UiConfigSchemaVersion, root.GetProperty("schemaVersion").GetInt32());

        var preview = root.GetProperty("previewButton");
        Assert.True(preview.GetProperty("enabled").GetBoolean());

        var toolbar = root.GetProperty("toolbar");
        Assert.True(toolbar.GetProperty("autoHideOnSelectionClear").GetBoolean());
        Assert.True(toolbar.GetProperty("pinWhileApplyingActions").GetBoolean());
        Assert.True(toolbar.GetProperty("actions").GetArrayLength() >= 8);

        var miniEditor = root.GetProperty("miniEditor");
        Assert.True(miniEditor.GetProperty("enabled").GetBoolean());
        Assert.False(miniEditor.GetProperty("usesSplitPreviewPane").GetBoolean());
      }

    [Fact]
    public void TryHandleRequest_DraftPreviewToolbarMiniEditorState_RoundTripWorks()
    {
        var endpoint = new MarkdownIpcEndpointService();

        using var setDraftRequest = JsonDocument.Parse("""
        {
          "type": "request",
          "id": "d1",
          "command": "markdown.draft.set",
          "payload": {
            "markdown": "draft body",
            "selectionStart": 2,
            "selectionEnd": 7
          }
        }
        """);
        Assert.True(endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandDraftSet, setDraftRequest.RootElement, out var setDraftResponse));
        using var setDraftDoc = JsonDocument.Parse(setDraftResponse);
        Assert.Equal("draft body", setDraftDoc.RootElement.GetProperty("markdown").GetString());

        using var getDraftRequest = JsonDocument.Parse("""{"type":"request","id":"d2","command":"markdown.draft.get"}""");
        Assert.True(endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandDraftGet, getDraftRequest.RootElement, out var getDraftResponse));
        using var getDraftDoc = JsonDocument.Parse(getDraftResponse);
        Assert.Equal(2, getDraftDoc.RootElement.GetProperty("selectionStart").GetInt32());
        Assert.Equal(7, getDraftDoc.RootElement.GetProperty("selectionEnd").GetInt32());

        using var setPreviewRequest = JsonDocument.Parse("""{"type":"request","id":"p1","command":"markdown.preview.state.set","payload":{"visible":false}}""");
        Assert.True(endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandPreviewStateSet, setPreviewRequest.RootElement, out var setPreviewResponse));
        using var setPreviewDoc = JsonDocument.Parse(setPreviewResponse);
        Assert.False(setPreviewDoc.RootElement.GetProperty("visible").GetBoolean());

        using var setToolbarRequest = JsonDocument.Parse("""{"type":"request","id":"t1","command":"markdown.toolbar.state.set","payload":{"visible":true,"pinned":true}}""");
        Assert.True(endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandToolbarStateSet, setToolbarRequest.RootElement, out var setToolbarResponse));
        using var setToolbarDoc = JsonDocument.Parse(setToolbarResponse);
        Assert.True(setToolbarDoc.RootElement.GetProperty("visible").GetBoolean());
        Assert.True(setToolbarDoc.RootElement.GetProperty("pinned").GetBoolean());

        using var setMiniEditorRequest = JsonDocument.Parse("""{"type":"request","id":"m1","command":"markdown.mini-editor.state.set","payload":{"open":true,"pinned":true,"content":"mini body"}}""");
        Assert.True(endpoint.TryHandleRequest(MarkdownIpcEndpointService.CommandMiniEditorStateSet, setMiniEditorRequest.RootElement, out var setMiniEditorResponse));
        using var setMiniDoc = JsonDocument.Parse(setMiniEditorResponse);
        Assert.True(setMiniDoc.RootElement.GetProperty("open").GetBoolean());
        Assert.Equal("mini body", setMiniDoc.RootElement.GetProperty("content").GetString());
    }
}
