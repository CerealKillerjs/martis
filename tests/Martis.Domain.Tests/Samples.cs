using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Genetics;

namespace Martis.Domain.Tests;

internal static class Samples
{
    public static Genome Genome(params (string Id, double Expression)[] genes)
    {
        var map = new Dictionary<GeneId, double>();
        foreach (var (id, expression) in genes)
        {
            map.Add(new GeneId(id), expression);
        }

        return new Genome(map);
    }

    public static Population Population(int cell = 0, Genome? genome = null, long id = 1)
    {
        return new Population(
            new PopulationId(id),
            new OrganismId("organism"),
            cell,
            genome ?? Genome(("a", 0.0)));
    }
}
