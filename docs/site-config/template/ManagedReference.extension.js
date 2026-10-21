const apiPage = require("./ApiPage.html.primary.js");

exports.preTransform = function (model) {
  return model;
};

exports.postTransform = function (model) {
  model = apiPage.transform(model);
  return removeMissingRootNamespaceLinks(model);
};

function removeMissingRootNamespaceLinks(model) {
  if (!model || typeof model !== "object") {
    return model;
  }

  const namespaceName = model.namespace && model.namespace.specName && model.namespace.specName[0];
  if (namespaceName && typeof namespaceName.value === "string") {
    namespaceName.value = namespaceName.value.replace(
      /<a\b[^>]*href=["'](?:\.\.\/)?Rowles\.html["'][^>]*>Rowles<\/a>\./g,
      "Rowles.",
    );
  }

  for (const child of model.children || []) {
    removeMissingRootNamespaceLinks(child);
  }

  return model;
}
