namespace Causa.Tool.Historical

/// The active historical catalog, in catalog order.
module Cases =
    let all : CaseDefinition list = [ Apollo1.definition; Apollo11.definition; Challenger.definition ]
