module.exports = {
  testEnvironment: 'node',
  roots: ['<rootDir>/tests/unit'],
  testMatch: ['**/*.test.ts'],
  setupFiles: ['<rootDir>/tests/unit/setup.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', { tsconfig: '<rootDir>/tsconfig.jest.json' }]
  },
  moduleNameMapper: {
    '^@pnp/sp(/.*)?$': '<rootDir>/tests/unit/mocks/pnpSp.ts'
  }
};
