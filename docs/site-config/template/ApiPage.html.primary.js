const accessOrder = [
  ["private", /\bprivate\b/i],
  ["internal", /\binternal\b/i],
  ["internal", /\bFriend\b/],
  ["protected", /\bprotected\b/i],
  ["public", /\bpublic\b/i],
];

const kindIcons = Object.freeze({
  Class: "Class",
  class: "Class",
  Interface: "Interface",
  interface: "Interface",
  Struct: "Structure",
  struct: "Structure",
  Enum: "Enumeration",
  enum: "Enumeration",
  Delegate: "Delegate",
  delegate: "Delegate",
  Constructor: "Method",
  constructor: "Method",
  Method: "Method",
  method: "Method",
  Property: "Property",
  property: "Property",
  Field: "Field",
  field: "Field",
  Event: "Event",
  event: "Event",
  Operator: "Operator",
  operator: "Operator",
  Namespace: "Namespace",
  namespace: "Namespace",
});

const accessSuffix = {
  public: "Public",
  internal: "Internal",
  private: "Private",
  protected: "Protected",
};

const modifierIcons = [
  ["sealed", /\bsealed\b/i, "Sealed", modifierIcon],
  ["partial", /\bpartial\b/i, "Partial", () => "PartiallyComplete.svg"],
  ["abstract", /\babstract\b/i, "Abstract", () => "AbstractClass.svg"],
  ["static", /\bstatic\b/i, "Static", () => "OverlayStatic.svg"],
  ["readonly", /\breadonly\b/i, "Read-only", () => "LiveShareReadOnly.svg"],
  ["const", /\bconst\b/i, "Constant", () => "Constant.svg"],
  ["async", /\basync\b/i, "Async", () => "AsynchronousMessage.svg"],
];

exports.transform = function (model) {
  decorateItem(model);
  decorateChildren(model);
  return model;
};

function decorateChildren(item) {
  for (const child of item.children || []) {
    decorateItem(child);
    decorateChildren(child);
  }
}

function decorateItem(item) {
  const kind = detectKind(item);
  if (!kind) {
    return;
  }

  const access = detectAccess(item);
  const iconRoot = Object.hasOwn(kindIcons, kind) ? kindIcons[kind] : "Type";

  item.apiKind = kind;
  item.apiAccess = access;
  item.apiIcon = `${iconRoot}${accessSuffix[access] || ""}.svg`;
  item.apiIconAlt = `${capitalise(access)} ${kind.toLowerCase()}`;
  item.apiDisplayName = cleanName(displayName(item));
  item.apiModifierIcons = detectModifiers(item, iconRoot);

  item.apiShowAccessIcon = access === "internal" || access === "private";

  if (item.apiShowAccessIcon) {
    item.apiAccessIcon = "Lock.svg";
    item.apiAccessIconAlt = capitalise(access);
  }
}

function detectModifiers(item, iconRoot) {
  const signature = signatureText(item);
  const icons = [];

  for (const [name, pattern, label, icon] of modifierIcons) {
    if (pattern.test(signature)) {
      icons.push({
        name,
        icon: icon(iconRoot),
        alt: label,
      });
    }
  }

  return icons;
}

function modifierIcon(iconRoot) {
  switch (iconRoot) {
    case "Class":
    case "Method":
    case "Property":
    case "Field":
    case "Event":
    case "Structure":
    case "Enumeration":
    case "Delegate":
    case "Operator":
      return `${iconRoot}Sealed.svg`;
    default:
      return "TypeSealed.svg";
  }
}

function detectKind(item) {
  if (item.type && kindIcons[item.type]) {
    return item.type;
  }

  if (item.isNamespace) return "Namespace";
  if (item.inClass) return "Class";
  if (item.inInterface) return "Interface";
  if (item.inStruct) return "Struct";
  if (item.inEnum) return "Enum";
  if (item.inDelegate) return "Delegate";
  if (item.inConstructor) return "Constructor";
  if (item.inMethod) return "Method";
  if (item.inProperty) return "Property";
  if (item.inField) return "Field";
  if (item.inEvent) return "Event";
  if (item.inOperator) return "Operator";

  return null;
}

function detectAccess(item) {
  if (item.apiAccessOverride) {
    return item.apiAccessOverride;
  }

  if (displayName(item).includes("🔒")) {
    return "internal";
  }

  const signature = signatureText(item);
  for (const [access, pattern] of accessOrder) {
    if (pattern.test(signature)) {
      return access;
    }
  }

  return "public";
}

function signatureText(item) {
  const syntax = item.syntax || {};
  const content = syntax.content;
  const csharp = Array.isArray(content) ? content.map(valueOf).join(" ") : valueOf(content);
  const vb = valueOf(syntax["content.vb"]);
  return `${csharp} ${vb}`;
}

function valueOf(value) {
  if (!value) return "";
  if (typeof value === "string") return value;
  if (typeof value.value === "string") return value.value;
  return "";
}

function displayName(item) {
  return valueOf(item.name) || valueOf(Array.isArray(item.name) ? item.name[0] : null) || item.id || "";
}

function cleanName(name) {
  return name.replace(/\s*🔒\s*/g, "").trim();
}

function capitalise(value) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
