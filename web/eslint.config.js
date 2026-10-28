// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/**
 * The folder layering, enforced rather than documented only:
 *
 *   core, shared        app-wide; know nothing about any feature
 *   features/x/ui       presentational; inputs in, outputs out, no services, no HTTP
 *   features/x/feature  routed pages; wire data-access to ui
 *   features/x/data-access  HTTP, state, models
 *
 * A feature never reaches into another feature. Promote what both need to `shared` instead.
 */
const layering = [
  {
    files: ['src/app/core/**/*.ts', 'src/app/shared/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        { patterns: [{ group: ['**/features/**'], message: 'core and shared must not depend on a feature.' }] },
      ],
    },
  },
  {
    files: ['src/app/features/*/ui/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [{ name: '@angular/common/http', message: 'ui components take data through inputs.' }],
          patterns: [
            { group: ['**/feature/**'], message: 'ui components must not depend on pages.' },
            {
              group: ['**/data-access/*', '!**/data-access/*.models'],
              message: 'ui components may import models from data-access, never services or stores.',
            },
          ],
        },
      ],
    },
  },
  {
    files: ['src/app/features/*/data-access/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        { patterns: [{ group: ['**/ui/**', '**/feature/**'], message: 'data-access must not depend on the view layer.' }] },
      ],
    },
  },
  {
    // Inside features/<name>/, climbing three levels lands in features/ itself: that is a sibling feature.
    files: ['src/app/features/**/*.ts'],
    ignores: ['src/app/features/*/*.routes.ts'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          // The same climb also reaches src/app/shared and src/app/core, which are always allowed.
          selector:
            'ImportDeclaration[source.value=/^(\\.\\.\\/){2,3}(?!\\.\\.|shared\\/|core\\/)[^/]+\\/(data-access|feature|ui)\\//]',
          message: 'Features must not import from each other. Move shared code to src/app/shared.',
        },
      ],
    },
  },
];

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.strict,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'app', style: 'camelCase' }],
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'app', style: 'kebab-case' }],
      '@angular-eslint/prefer-signals': 'error',
      '@angular-eslint/no-implicit-take-until-destroyed': 'error',
      '@typescript-eslint/explicit-member-accessibility': ['error', { accessibility: 'no-public' }],
      // shared/ui primitives take `class` as an input so they can merge it with their own (see cn.ts).
      '@angular-eslint/no-input-rename': ['error', { allowedNames: ['class'] }],
      // A component with no logic is still a component: the decorator is the content.
      '@typescript-eslint/no-extraneous-class': ['error', { allowWithDecorator: true }],
    },
  },
  ...layering,
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/prefer-control-flow': 'error',
      '@angular-eslint/template/prefer-self-closing-tags': 'error',
    },
  },
]);
