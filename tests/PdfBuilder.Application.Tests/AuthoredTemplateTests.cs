using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Tests;

public sealed class AuthoredTemplateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly TemplateContent V1 = new("<p>{{ a }}</p>", "p { color: red; }", "{\"a\":1}", PdfRenderOptions.Default);
    private static readonly TemplateContent V2 = V1 with { Html = "<p>{{ a }}!</p>" };

    [Fact]
    public void Starts_as_an_unpublished_draft_at_revision_1()
    {
        var template = AuthoredTemplate.Create("letter", V1, T0);

        Assert.Equal(TemplateStatus.Draft, template.Status);
        Assert.Null(template.Published);
        Assert.Equal(1, template.Revision);
    }

    [Fact]
    public void Publishing_copies_the_draft_and_bumps_the_revision()
    {
        var published = AuthoredTemplate.Create("letter", V1, T0).Publish(1, T0.AddMinutes(1));

        Assert.Equal(TemplateStatus.Published, published.Status);
        Assert.Equal(V1, published.Published);
        Assert.Equal(2, published.Revision);
        Assert.Equal(T0.AddMinutes(1), published.PublishedAt);
    }

    [Fact]
    public void Editing_after_publishing_leaves_the_published_version_alone()
    {
        var edited = AuthoredTemplate.Create("letter", V1, T0).Publish(1, T0).SaveDraft(V2, 2, T0);

        Assert.Equal(TemplateStatus.Modified, edited.Status);
        Assert.Equal(V1, edited.Published);
        Assert.Equal(V2, edited.Draft);
    }

    [Fact]
    public void Saving_the_same_content_again_is_not_a_change()
    {
        var template = AuthoredTemplate.Create("letter", V1, T0);

        Assert.Same(template, template.SaveDraft(V1 with { }, 1, T0.AddHours(1)));
    }

    [Fact]
    public void Republishing_an_unchanged_draft_is_not_a_change()
    {
        var published = AuthoredTemplate.Create("letter", V1, T0).Publish(1, T0);

        Assert.Same(published, published.Publish(2, T0.AddHours(1)));
    }

    [Fact]
    public void Refuses_a_change_based_on_an_old_revision()
    {
        var template = AuthoredTemplate.Create("letter", V1, T0).SaveDraft(V2, 1, T0);

        var error = Assert.Throws<StaleTemplateRevisionException>(() => template.SaveDraft(V1, 1, T0));
        Assert.Equal(2, error.CurrentRevision);
        Assert.Throws<StaleTemplateRevisionException>(() => template.Publish(1, T0));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("")]
    [InlineData("-leading-dash")]
    public void Refuses_a_key_that_could_not_be_a_file_name(string key)
    {
        Assert.Throws<ArgumentException>(() => AuthoredTemplate.Create(key, V1, T0));
    }
}

public sealed class TemplateContentTests
{
    [Fact]
    public void Puts_the_stylesheet_just_before_the_end_of_the_head()
    {
        var content = new TemplateContent("<html><HEAD><title>t</title></HEAD><body></body></html>", "b{}", "", PdfRenderOptions.Default);

        Assert.Equal("<html><HEAD><title>t</title><style>\nb{}\n</style>\n</HEAD><body></body></html>", content.ComposeHtml());
    }

    [Fact]
    public void Puts_the_stylesheet_first_when_there_is_no_head()
    {
        var content = new TemplateContent("<p>x</p>", "p{}", "", PdfRenderOptions.Default);

        Assert.Equal("<style>\np{}\n</style>\n<p>x</p>", content.ComposeHtml());
    }

    [Fact]
    public void Leaves_the_markup_untouched_without_a_stylesheet()
    {
        var content = new TemplateContent("<p>x</p>", "  ", "", PdfRenderOptions.Default);

        Assert.Equal("<p>x</p>", content.ComposeHtml());
    }

    [Fact]
    public void Falls_back_to_an_empty_object_when_there_is_no_sample()
    {
        Assert.Equal("{}", new TemplateContent("<p/>", "", " ", PdfRenderOptions.Default).SampleOrEmpty);
    }
}
