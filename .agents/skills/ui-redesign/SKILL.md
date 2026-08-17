---
name: ui-redesign
description: Refatoração visual controlada de interface existente a partir de um design system e de referências visuais. Use ao aplicar nova identidade, redesenhar tela ou avaliar consistência visual — preservando comportamento, rotas, chamadas de API e acessibilidade.
---

# Refatoração de UI orientada a design system

## Missão

Ao melhorar uma interface:

> Entenda a aplicação existente primeiro, depois aplique o design system sem
> quebrar funcionalidade.

Uma captura de tela não é motivo para reescrever a aplicação.

O agente deve preservar:

- regras de negócio
- chamadas de API
- fluxo de dados
- rotas
- autenticação
- autorização
- gerenciamento de estado
- validação
- ações existentes do usuário
- contratos de backend
- comportamento de acessibilidade

A menos que o usuário peça explicitamente mudanças funcionais.

## Prioridade das entradas

```
Requisitos explícitos do usuário
        ↓
Comportamento existente da aplicação
        ↓
Design system
        ↓
Capturas de referência
        ↓
Suposições do agente
```

Nunca invente um requisito quando o projeto existente ou as instruções do
usuário já respondem.

## Primeiro passo — inspecionar antes de editar

Antes de modificar código, identifique:

- framework de frontend, build, gerenciador de pacotes
- estratégia de estilo e arquitetura de componentes
- roteamento e gerenciamento de estado
- componentes compartilhados e design tokens
- CSS global, estratégia responsiva, padrões de acessibilidade
- configuração de teste

E também: ponto de entrada da página, componentes usados pela tela alvo,
estilos que a afetam, componentes reaproveitáveis.

Não reescreva a página de imediato.

## Análise de captura de tela

Extraia como evidência visual:

**Layout** — largura da página, largura de conteúdo, dimensões de
sidebar/cabeçalho, colunas, alinhamento, espaçamento, ritmo vertical.

**Tipografia** — hierarquia, tamanhos aproximados, pesos, entrelinha, tracking,
caixa.

**Cor** — fundo, superfícies, acento primário, texto, texto secundário, bordas,
cores de estado.

**Componentes** — botões, cartões, campos, navegação, listas, tabelas, avatares,
etiquetas, diálogos.

**Pistas de interação** — infira apenas o que a evidência visual sustenta. Não
invente comportamento só porque outro produto o tem.

## Regra da captura de referência

Referência é inspiração e evidência, não identidade a copiar.

Não copie: logotipos, nomes de marca, ilustrações proprietárias, texto de
marketing, estrutura exata de produto, navegação alheia.

Copie princípios visuais, não identidade de produto. Para uma aplicação
administrativa, adapte a linguagem visual à arquitetura de informação da própria
aplicação.

## Regra do design system

Se um design system em Markdown for fornecido, **ele é a fonte visual de
verdade**: cores, tipografia, espaçamento, raios, superfícies, regras de
componente, estados de interação, acessibilidade.

Não crie tokens concorrentes sem necessidade. Se o projeto já tem uma
arquitetura de tokens, prefira **mapear o novo sistema sobre a arquitetura
existente** em vez de duplicar tudo.

## Regra do código existente

Prefira refatoração incremental.

Bom:

```
Componente existente → preservar comportamento → refatorar estrutura
→ aplicar tokens → melhorar responsividade
```

Evite:

```
Apagar tudo → reescrever do zero → torcer para a funcionalidade sobreviver
```

Um redesenho visual deve normalmente ser a **menor mudança segura** que produz o
resultado desejado.

## Estratégia de componentes

Antes de criar um componente novo, verifique se já existe equivalente: Button,
Card, Input, Modal, Avatar, Navigation, Typography.

Evite versões ligeiramente diferentes do mesmo componente. Atualize o componente
compartilhado apenas quando o novo comportamento visual for compatível com todos
os consumidores; caso contrário, crie uma variante com escopo.

## Estratégia de tokens

Use tokens semânticos em vez de valores crus repetidos pelo código.

Se o projeto usa Tailwind, mapeie o design system no tema. Se usa variáveis CSS,
centralize os tokens. Se usa biblioteca de componentes, configure o tema antes de
escrever muito CSS pontual.

## Layout e responsividade

Use primitivas de layout adequadas ao framework — Grid, Flexbox, container
queries, breakpoints, dimensionamento intrínseco. Evite posicionamento absoluto
em excesso. **Não reproduza uma captura com coordenadas fixas.**

O resultado precisa continuar utilizável quando: o texto muda, o tamanho dos
dados muda, a localização muda, a viewport muda, as configurações de
acessibilidade mudam.

- **Desktop** — use o espaço horizontal, preserve hierarquia, múltiplas colunas
  quando útil.
- **Tablet** — reduza espaçamento, permita quebra controlada, preserve largura
  legível de cartão.
- **Mobile** — coluna única quando apropriado, navegação recolhida, alvos de
  toque preservados, sem texto cortado, sem overflow horizontal.

Não basta reduzir a escala do desktop.

## Acessibilidade

**Estrutura semântica** — títulos corretos, botão é botão, link é link, controle
de formulário tem rótulo.

**Teclado** — todo elemento interativo alcançável, ordem de tabulação lógica,
foco visível, sem armadilha de teclado.

**Formulários** — rótulo associado ao controle.

```html
<!-- bom -->
<label for="memberName">Nome</label>
<input id="memberName" name="memberName" />

<!-- ruim -->
<label>Nome</label>
<input />
```

**Contraste** — não use texto de baixo contraste só porque fica elegante numa
captura.

**Interação** — não dependa exclusivamente de cor, hover, ícone ou animação para
comunicar informação importante.

## Qualidade visual

Procure ativamente por: espaçamento inconsistente, raios inconsistentes, sombras
excessivas, cores arbitrárias, inconsistência tipográfica, ícones grandes
demais, bordas desnecessárias, seções apertadas, alinhamento fraco,
responsividade ruim, aparência genérica de biblioteca de componentes.

A UI final deve parecer intencional.

## Preservar funcionalidade

Antes de mudar um componente, identifique suas responsabilidades funcionais.
Nunca remova acidentalmente: handlers de evento, submissão de formulário,
chamadas de API, validação, estados de carregamento, estados de erro,
verificações de permissão, navegação, filtro, ordenação, paginação,
comportamento de teclado.

Refatoração visual não pode mudar comportamento em silêncio.

## Estados

Todo componente interativo considera: padrão, hover, foco, ativo, desabilitado,
carregando, sucesso, erro, vazio, preenchido.

Não desenhe apenas o caminho feliz.

## UI orientada a dados

Não assuma que o dado da captura é o dado real. Os componentes precisam
continuar funcionando com: zero registros, um registro, muitos registros, nomes
longos, descrições longas, valores opcionais ausentes, dados carregando, erros
de API.

Nunca fixe conteúdo de captura em componente de produção, a menos que o usuário
peça dado fictício.

## Fluxo de implementação

1. **Descobrir** — inspecionar o repositório e localizar a tela alvo.
2. **Entender** — explicar arquitetura atual, implementação visual atual,
   componentes reaproveitáveis, estilos relevantes, riscos.
3. **Planejar** — plano conciso de implementação.
4. **Implementar** — o menor conjunto coerente de mudanças.
5. **Validar** — build, testes, lint, checagem de tipos, acessibilidade,
   responsividade, consistência visual.
6. **Revisar** — contra requisitos, design system, referência e comportamento
   existente.
7. **Reportar** — arquivos alterados, o que mudou, comportamento preservado,
   verificações executadas, limitações conhecidas.

## Laço de revisão visual

Quando houver renderização de captura disponível:

```
Implementar → renderizar → comparar com a referência
→ identificar as maiores diferenças → corrigir → renderizar de novo
```

Prioridade: 1) layout geral, 2) espaçamento, 3) tipografia, 4) dimensão dos
componentes, 5) cor, 6) bordas/raios, 7) micro-detalhes.

Não gaste tempo com 2px de diferença num ícone enquanto a estrutura da página
ainda está errada.

## Evitar superengenharia

Não: introduza dependências desnecessárias, crie um design system novo quando já
existe um, reescreva componentes não relacionados, migre frameworks, renomeie
arquivos não relacionados, mude código de backend, mude contratos de API,
adicione animação sem propósito.

## Animação

Use com parcimônia: transições curtas, opacidade/transform, suporte a movimento
reduzido. Evite movimento constante, animação decorativa, grandes deslocamentos
de layout e animações que atrasam o fluxo normal.

Se o design system não define animação, mantenha-a discreta.

## Tratamento de conflito

Quando a mudança visual pedida conflita com a arquitetura existente:

1. Não quebre a arquitetura em silêncio.
2. Identifique o conflito.
3. Escolha a menor solução compatível.
4. Explique o trade-off se ele afeta o resultado de forma relevante.

## Critérios de conclusão

- a direção visual pedida está implementada
- a funcionalidade existente está preservada
- os tokens são usados de forma consistente
- a responsividade é aceitável
- os problemas de acessibilidade introduzidos pelo redesenho foram resolvidos
- nenhuma dependência desnecessária foi adicionada
- o projeto continua compilando e passando nos testes disponíveis

## Prioridade quando as decisões conflitam

1. Acessibilidade
2. Funcionalidade de negócio existente
3. Hierarquia de informação
4. Layout e espaçamento
5. Tipografia
6. Cor
7. Detalhe decorativo

## Princípio final

O agente não é um gerador de código a partir de captura de tela. O agente é um
engenheiro executando um redesenho controlado.

```
Referência + Design System + Arquitetura existente + Requisitos
        ↓
UI consistente, acessível e sustentável
```

Nunca otimize semelhança visual ao custo da correção da aplicação.
