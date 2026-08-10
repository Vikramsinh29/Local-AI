using System.Xml.Linq;

namespace LocalAI.Tests;

public sealed class MainWindowXamlTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ProjectInstructionReadOnlyBindings_AreExplicitlyOneWay()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);
        string[] runBindings = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Run")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Contains(
            runBindings,
            binding => binding.Contains(
                    "InclusionText",
                    StringComparison.Ordinal) &&
                binding.Contains(
                    "Mode=OneWay",
                    StringComparison.Ordinal));
        Assert.Contains(
            runBindings,
            binding => binding.Contains(
                    "StateReason",
                    StringComparison.Ordinal) &&
                binding.Contains(
                    "Mode=OneWay",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ConversationAndComposer_HaveIndependentBoundedScrollRegions()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);

        XElement mainChatGrid = FindNamedElement(
            document,
            "MainChatGrid");
        XElement conversationScrollViewer = FindNamedElement(
            document,
            "ConversationScrollViewer");
        XElement composerHostGrid = FindNamedElement(
            document,
            "ComposerHostGrid");
        XElement composerScrollViewer = FindNamedElement(
            document,
            "ComposerScrollViewer");

        XElement[] rowDefinitions = mainChatGrid
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Grid.RowDefinitions")
            .Elements()
            .ToArray();

        Assert.Equal(3, rowDefinitions.Length);
        Assert.Equal("2*", rowDefinitions[1].Attribute("Height")?.Value);
        Assert.Equal("160", rowDefinitions[1].Attribute("MinHeight")?.Value);
        Assert.Equal("3*", rowDefinitions[2].Attribute("Height")?.Value);
        Assert.Equal("280", rowDefinitions[2].Attribute("MinHeight")?.Value);

        Assert.Equal(
            "1",
            conversationScrollViewer.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            "Auto",
            conversationScrollViewer
                .Attribute("VerticalScrollBarVisibility")?.Value);

        Assert.Equal(
            "2",
            composerHostGrid.Attribute("Grid.Row")?.Value);
        Assert.Equal(
            "Auto",
            composerScrollViewer
                .Attribute("VerticalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void ProjectMemory_PromptAndEditorSelectionsAreSeparate()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);

        XElement promptSelector = FindNamedElement(
            document,
            "PromptMemorySelector");
        XElement editorList = FindNamedElement(
            document,
            "ProjectMemoryEditorList");

        Assert.Contains(
            "SelectedPromptProjectMemoryEntry",
            promptSelector.Attribute("SelectedItem")?.Value ?? string.Empty);
        Assert.Contains(
            "SelectedProjectMemoryEntry",
            editorList.Attribute("SelectedItem")?.Value ?? string.Empty);
        Assert.DoesNotContain(
            "SelectedPromptProjectMemoryEntry",
            editorList.Attribute("SelectedItem")?.Value ?? string.Empty);
    }

    [Fact]
    public void RepositoryPanelHeader_ConstrainsLongNameAndPathBeforeCloseButton()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);

        XElement headerText = FindNamedElement(
            document,
            "RepositoryPanelHeaderText");
        XElement closeButton = FindNamedElement(
            document,
            "RepositoryPanelCloseButton");
        XElement[] textBlocks = headerText
            .Elements()
            .Where(element => element.Name.LocalName == "TextBlock")
            .ToArray();
        XElement[] columnDefinitions = closeButton
            .Parent!
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements()
            .ToArray();

        Assert.Equal(2, textBlocks.Length);
        Assert.All(
            textBlocks,
            textBlock =>
            {
                Assert.Equal(
                    "CharacterEllipsis",
                    textBlock.Attribute("TextTrimming")?.Value);
                Assert.Equal(
                    "NoWrap",
                    textBlock.Attribute("TextWrapping")?.Value);
            });
        Assert.Contains(
            "RepositoryName",
            textBlocks[0].Attribute("ToolTip")?.Value ?? string.Empty);
        Assert.Contains(
            "RepositoryPath",
            textBlocks[1].Attribute("ToolTip")?.Value ?? string.Empty);
        Assert.Equal(3, columnDefinitions.Length);
        Assert.Equal("12", columnDefinitions[1].Attribute("Width")?.Value);
        Assert.Equal("32", columnDefinitions[2].Attribute("Width")?.Value);
        Assert.Equal("2", closeButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("32", closeButton.Attribute("Width")?.Value);
        Assert.Equal("Right", closeButton.Attribute("HorizontalAlignment")?.Value);
    }

    [Fact]
    public void RepositorySearch_IsBoundedAndRequiresExplicitContextAction()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);

        XElement panel = FindNamedElement(document, "RepositorySearchPanel");
        XElement results = FindNamedElement(document, "RepositorySearchResults");
        XElement[] buttons = panel.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.Equal("82", results.Attribute("MaxHeight")?.Value);
        Assert.Contains(
            "RepositorySearchResults",
            results.Attribute("ItemsSource")?.Value ?? string.Empty);
        Assert.Contains(
            buttons,
            button => (button.Attribute("Command")?.Value ?? string.Empty)
                .Contains(
                    "AddSelectedSearchResultToContextCommand",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ContentSearch_IsBoundedAndTargetsSelectedContextFile()
    {
        string xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);

        XElement panel = FindNamedElement(document, "ContentSearchPanel");
        XElement results = FindNamedElement(document, "ContentSearchResults");
        XElement query = panel.Descendants()
            .Single(element => element.Name.LocalName == "TextBox");
        XElement[] buttons = panel.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.Equal("100", query.Attribute("MaxLength")?.Value);
        Assert.Equal("70", results.Attribute("MaxHeight")?.Value);
        Assert.Equal(
            "Disabled",
            results.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(
            "Stretch",
            results.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Contains(
            "ContentSearchMatches",
            results.Attribute("ItemsSource")?.Value ?? string.Empty);
        Assert.Contains(
            buttons,
            button => (button.Attribute("Command")?.Value ?? string.Empty)
                .Contains(
                    "SearchSelectedFileContentCommand",
                    StringComparison.Ordinal));
    }

    private static XElement FindNamedElement(
        XDocument document,
        string name)
    {
        return document
            .Descendants()
            .Single(element =>
                element.Attribute(XamlNamespace + "Name")?.Value == name);
    }
}
