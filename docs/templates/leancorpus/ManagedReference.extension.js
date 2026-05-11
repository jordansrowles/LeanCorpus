const apiPage = require("./ApiPage.html.primary.js");

exports.preTransform = function (model) {
  return model;
};

exports.postTransform = function (model) {
  return apiPage.transform(model);
};
