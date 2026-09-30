using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Punto de partida ÚNICO -- reutilizado por <see cref="LayerLayoutService"/>
/// (para mutar) y por los endpoints de solo lectura (GET .../layers/layout,
/// GET .../layers/consolidated) -- para resolver, capa por capa, el layout
/// vigente: la última entrada guardada para ese groupId, o -- si esa capa
/// nunca se tocó -- los valores DEFAULT ya documentados desde M2.1-S03
/// (Visible=true, Locked=false, Order=posición en
/// <see cref="VectorLayerSetVersion.Layers"/>). Garantiza que TODAS las capas
/// del conjunto vigente tengan siempre una entrada, sin importar cuántas se
/// tocaron alguna vez -- un solo lugar para esta regla, nunca duplicada.
/// </summary>
public static class LayerLayoutDefaults
{
    public static IReadOnlyList<LayerLayoutEntry> Resolve(VectorLayerSetVersion layerSet, LayerLayoutSetVersion? layout)
    {
        var existingByGroupId = (layout?.Entries ?? Array.Empty<LayerLayoutEntry>()).ToDictionary(e => e.GroupId);
        return layerSet.Layers
            .Select((layer, index) => existingByGroupId.TryGetValue(layer.GroupId, out var entry)
                ? entry
                : new LayerLayoutEntry(layer.GroupId, index, Visible: true, Locked: false, Name: null))
            .ToList();
    }
}
