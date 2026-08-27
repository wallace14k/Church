/**
 * Testes da lógica pura do app — agendador de repetição espaçada, ofensiva e
 * montagem da fila de treino. São as partes que definem o comportamento do
 * produto e não dependem do React Native, então rodam em Node direto.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  testMatch: ['<rootDir>/src/**/*.test.ts'],
};
