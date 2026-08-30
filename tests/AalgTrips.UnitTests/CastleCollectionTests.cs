using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Coverage for <see cref="CastleCollection"/>: a catalogue built from JSON is
    /// ordered nearest-to-Ellon first, the string access tier and the "is it worth
    /// listing" rule are read correctly, and the real embedded catalogue loads with
    /// the full UK castle set.
    /// </summary>
    [TestFixture]
    public class CastleCollectionTests
    {
        private const string Json = @"[
            { ""id"": ""Q3"", ""name"": ""Far Castle"",  ""lat"": 51.50, ""lon"": -0.12, ""nation"": ""England"",  ""admin"": ""London"",       ""operator"": null,  ""access"": ""Unknown"",     ""website"": null,             ""heritage"": false },
            { ""id"": ""Q1"", ""name"": ""Near Castle"", ""lat"": 57.36, ""lon"": -2.08, ""nation"": ""Scotland"", ""admin"": ""Aberdeenshire"", ""operator"": ""NTS"", ""access"": ""MembersFree"", ""website"": ""https://x"", ""heritage"": true },
            { ""id"": ""Q2"", ""name"": ""Mid Castle"",  ""lat"": 55.95, ""lon"": -3.19, ""nation"": ""Scotland"", ""admin"": ""Edinburgh"",     ""operator"": null,  ""access"": ""Unknown"",     ""website"": null,             ""heritage"": true }
        ]";

        [Test]
        public void Castles_are_ordered_nearest_to_Ellon_first()
        {
            var castles = CastleCollection.FromJson(Json);

            Assert.That(castles.Castles.Select(c => c.Id), Is.EqualTo(new[] { "Q1", "Q2", "Q3" }));
        }

        [Test]
        public void The_access_tier_is_read_from_its_string_name()
        {
            var castles = CastleCollection.FromJson(Json);

            Assert.That(castles.Castles.Single(c => c.Id == "Q1").Access, Is.EqualTo(AccessTier.MembersFree));
        }

        [Test]
        public void IsVisitable_needs_an_operator_website_or_heritage_listing()
        {
            var castles = CastleCollection.FromJson(Json);

            Assert.Multiple(() =>
            {
                Assert.That(castles.Castles.Single(c => c.Id == "Q1").IsVisitable, Is.True, "operator, website and heritage");
                Assert.That(castles.Castles.Single(c => c.Id == "Q2").IsVisitable, Is.True, "heritage-listed only, still visitable");
                Assert.That(castles.Castles.Single(c => c.Id == "Q3").IsVisitable, Is.False, "no operator, website or heritage");
            });
        }

        [Test]
        public void The_embedded_catalogue_loads_the_full_UK_set_nearest_first()
        {
            var castles = new CastleCollection();

            Assert.Multiple(() =>
            {
                Assert.That(castles.Castles.Count, Is.GreaterThan(2000), "the embedded catalogue holds the full UK castle set");
                Assert.That(castles.Castles.Any(c => c.Name == "Edinburgh Castle"), Is.True, "a well-known castle is present");
                Assert.That(castles.Castles[0].DistanceMiles, Is.LessThan(5), "the nearest castle is close to Ellon");
            });
        }
    }
}