using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using Store.Api.Data;

namespace Store.Api.GraphQL;

[ObjectType<Brand>]
public static partial class BrandNode
{
    // Brand.products and Product.brand form a cycle (brand -> products -> brand -> ...),
    // which is why the server sets a maximum execution depth.
    [ListSize(AssumedSize = 20)]
    public static async Task<Product[]> GetProductsAsync(
        [Parent] Brand brand, IProductsByBrandIdDataLoader productsByBrandId, CancellationToken ct)
        => await productsByBrandId.LoadAsync(brand.Id, ct) ?? [];
}
