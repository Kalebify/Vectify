/**
 * Contratos tipados que expone ASP.NET Core para la agrupación LÓGICA de
 * componentes físicos (M2-S05): POST/GET
 * .../vectors/{vectorId}/components/groups. Deben reflejar exactamente
 * Vectify.Api.Contracts.ComponentGroupSetResponse/ComponentGroupPayload.
 */

/** Un grupo lógico de componentes -- ver Vectify.Api.Components.ComponentGroup. NO tiene geometría propia: solo referencia componentIds YA calculados por M2-S03. */
export interface ComponentGroupPayload {
  groupId: string;
  name: string;
  componentIds: string[];
  /**
   * True si algún componentId del grupo ya no aparece en la
   * ComponentSetVersion vigente de este vector -- el grupo sigue existiendo
   * igual (nunca se migra ni se borra automáticamente), esto es solo un
   * aviso informativo (ver Vectify.Api.Components.ComponentGroupStaleness).
   */
  isStale: boolean;
  missingComponentIds: string[];
}

/** Respuesta de POST/GET .../vectors/{vectorId}/components/groups: siempre el último conjunto de grupos vigente para esa capa. */
export interface ComponentGroupSetResponse {
  projectId: string;
  imageId: string;
  vectorId: string;
  version: number;
  groups: ComponentGroupPayload[];
}

/** Code es estable y se mapea a copy en React sin parsear message. Valores que pueden devolver los endpoints de grupos de componentes. */
export type ComponentGroupErrorCode =
  | "not_found"
  | "component_not_found"
  | "group_not_found"
  | "invalid_parameters"
  | "internal_error"
  | "network_error";
