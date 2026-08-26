using System.Xml.Linq;
using Xunit;

namespace SmartAnalysis.Tests.DesignSystem;

/// <summary>
/// Structural checks on the view XAML that a build cannot make and a ViewModel test cannot see. These catch
/// layouts that compile, bind, and pass every unit test while rendering wrong — the class of defect that
/// previously reached the screen as a stage collapsed to the width of its colour bar.
/// </summary>
public sealed class LayoutStructureTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAnalysis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate repo root (SmartAnalysis.sln).");
        return dir!.FullName;
    }

    private static IEnumerable<string> ViewXamlFiles()
        => Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "src", "SmartAnalysis.UI"), "*.xaml", SearchOption.AllDirectories);

    private static string Describe(XElement e)
    {
        string name = e.Attribute(X + "Name")?.Value ?? e.Attribute("Name")?.Value ?? "(unnamed)";
        return $"{e.Name.LocalName} '{name}'";
    }

    /// <summary>
    /// A DockPanel gives its remaining space to its <b>last</b> child only; every earlier child without an
    /// explicit Dock is docked Left and shrinks to its own desired width. Two fill children is therefore never
    /// what the author meant — it silently collapses the first one.
    /// </summary>
    [Fact]
    public void No_dock_panel_has_more_than_one_undocked_child()
    {
        var offenders = new List<string>();

        foreach (string path in ViewXamlFiles())
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(path);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (var panel in doc.Descendants(P + "DockPanel"))
            {
                // Property-element children (<DockPanel.Resources>) are not layout children.
                var children = panel.Elements().Where(c => !c.Name.LocalName.Contains('.')).ToList();
                int undocked = children.Count(c => c.Attribute("DockPanel.Dock") is null);

                if (undocked > 1)
                {
                    string names = string.Join(", ", children
                        .Where(c => c.Attribute("DockPanel.Dock") is null)
                        .Select(Describe));
                    offenders.Add($"{Path.GetFileName(path)}: {Describe(panel)} has {undocked} fill children ({names})");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A DockPanel fills with its last child only; earlier undocked children collapse. Use a Grid:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }
}
