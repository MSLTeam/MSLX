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
  },
};
