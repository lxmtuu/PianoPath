using System.Globalization;

namespace PianoPath.Tests;

/// <summary>
/// The string table and the language registry: the half of <c>Loc</c> that is plain computation, which
/// is exactly the half this project can reach without WPF.
///
/// <para>
/// These tests touch process-wide state (<see cref="Loc.Apply"/>), so they restore it on the way out and
/// no other test class asserts on translated text. Note also that they never pass a string
/// <em>literal</em> to <see cref="Loc.T"/>: <c>tools/check_sources.py</c> collects every literal handed to
/// it and requires it to be a key of the English inventory, so an intentionally unknown key has to be
/// built at runtime.
/// </para>
/// </summary>
public class LocalizationTests : IDisposable
{
    public void Dispose() => Loc.Apply("");

    [Fact]
    public void English_is_the_default_and_the_first_entry_of_the_picker()
    {
        Assert.Same(Languages.English, Languages.Default);
        Assert.Same(Languages.English, Languages.All[0]);
        Assert.Equal(2, Languages.All.Length);
    }

    [Theory]
    [InlineData("vi")]              // the stored id
    [InlineData("vi-VN")]           // a culture tag
    [InlineData("Vietnamese")]      // the English name
    [InlineData("Tiếng Việt")]      // the name as its speakers write it
    [InlineData("  VI  ")]          // padded and in another case
    public void A_stored_value_resolves_to_its_language_however_it_was_written(string stored)
    {
        Assert.Same(Languages.Vietnamese, Languages.Find(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("klingon")]
    public void A_value_that_names_no_bundled_language_falls_back_to_English(string? stored)
    {
        Assert.Same(Languages.English, Languages.Find(stored));
    }

    [Fact]
    public void Normalize_returns_the_id_whatever_was_stored()
    {
        Assert.Equal("vi", Languages.Normalize("vi-VN"));
        Assert.Equal("en", Languages.Normalize("klingon"));
    }

    [Fact]
    public void Only_the_language_subtag_of_a_culture_matters()
    {
        Assert.Same(Languages.Vietnamese, Languages.FromCulture(new CultureInfo("vi-VN")));
        Assert.Same(Languages.English, Languages.FromCulture(new CultureInfo("en-GB")));
        Assert.Same(Languages.English, Languages.FromCulture(null));
    }

    [Fact]
    public void The_picker_prints_the_native_name_with_the_English_one_beside_it()
    {
        Assert.Equal("Tiếng Việt · Vietnamese", Languages.Vietnamese.Display);
        // A language whose two names are the same is not printed twice.
        Assert.Equal("English", Languages.English.Display);
    }

    [Fact]
    public void Every_key_of_the_inventory_is_known_and_a_key_nothing_holds_is_not()
    {
        var key = StringsEnglish.Table.Keys.First();
        var unknown = string.Concat("keyflow", "-", "no-such-key");

        Assert.True(Loc.Known(key));
        Assert.False(Loc.Known(unknown));
    }

    [Fact]
    public void In_English_a_key_translates_to_itself_and_an_unknown_one_comes_back_unchanged()
    {
        Loc.Apply("en");
        var key = StringsEnglish.Table.Keys.First();
        var unknown = string.Concat("keyflow", "-", "no-such-key");

        Assert.Equal(key, Loc.T(key));
        Assert.Equal(unknown, Loc.T(unknown));
        Assert.Contains(unknown, Loc.UnknownKeys);
    }

    [Fact]
    public void An_empty_key_is_returned_as_it_is_and_not_counted_as_unknown()
    {
        Loc.ResetDiagnostics();

        Assert.Equal("", Loc.T(""));

        Assert.Empty(Loc.UnknownKeys);
    }

    [Fact]
    public void F_fills_the_placeholders_of_a_template_and_needs_none_without_arguments()
    {
        Loc.Apply("en");
        var template = string.Concat("{0}", " of ", "{1}");

        Assert.Equal(template, Loc.F(template));
        Assert.Equal("3 of 7", Loc.F(template, 3, 7));
    }

    [Fact]
    public void Page_goes_through_the_same_table_as_any_other_label()
    {
        Loc.Apply("en");
        var key = StringsEnglish.Table.Keys.First();

        Assert.Equal(Loc.T(key), Loc.Page(key));
    }

    [Fact]
    public void Both_bundled_languages_carry_the_same_key_set()
    {
        // tools/check_sources.py proves the same thing statically; this is the runtime reading of it,
        // so a table that the checker never sees (a generated one, say) still cannot go out of step.
        foreach (var language in Languages.All)
            Assert.Empty(Loc.MissingFor(language));
    }

    [Fact]
    public void The_English_table_is_the_inventory_so_every_key_maps_to_itself()
    {
        foreach (var (key, value) in StringsEnglish.Table)
            Assert.Equal(key, value);
    }

    [Fact]
    public void Applying_a_language_publishes_it_and_remembers_what_was_stored()
    {
        Loc.Apply("vi-VN");

        Assert.Same(Languages.Vietnamese, Loc.Current);
        Assert.Equal("vi-VN", Loc.StoredId);

        Loc.Apply("  ");

        Assert.Equal("", Loc.StoredId);
    }

    [Fact]
    public void Switching_language_raises_the_event_open_surfaces_restyle_by()
    {
        var raised = 0;
        Loc.Changed += OnChanged;
        try
        {
            Loc.Apply("vi");
            Assert.Equal(1, raised);
        }
        finally { Loc.Changed -= OnChanged; }

        void OnChanged() => raised++;
    }

    [Fact]
    public void A_subscriber_is_run_by_a_refresh_and_survives_it()
    {
        var applied = 0;
        Loc.OnChanged(() => applied++);

        Loc.Refresh();

        Assert.Equal(1, applied);
    }
}
