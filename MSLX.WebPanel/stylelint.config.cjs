module.exports = {
  defaultSeverity: 'error',
  extends: [
    'stylelint-config-standard',
    'stylelint-config-standard-less'
  ],
  plugins: ['stylelint-less'],
  rules: {
    'at-rule-empty-line-before': null,
    'custom-property-empty-line-before': null,
    'declaration-empty-line-before': null,
    'no-descending-specificity': null,
    'at-rule-no-unknown': null,
    'selector-class-pattern': null,
    'color-function-notation': null,
    'alpha-value-notation': null,
    'rule-empty-line-before': null,
    'color-function-alias-notation': null,
    'color-hex-length': null,
    'comment-empty-line-before': null,
  },
};
