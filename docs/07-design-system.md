# Design System — Congrega (referência Grove)

Fonte visual de verdade da interface.

**Terceira substituição.** Mercury (índigo sobre branco, cartão de 12px com
sombra) → Perk (lima elétrico sobre pergaminho, cartão de 28px, sem sombra) →
**Grove**, o atual: superfícies brancas sobre canvas verde-acinzentado, cartão
de 22px com sombra difusa, verde-oliva como cor de ação, âmbar como segunda cor
categórica, navegação em barra superior.

O conteúdo abaixo da §1 ainda descreve o sistema **Perk** em vários pontos. Ele
fica como registro do que foi decidido e por quê — as decisões D1 a D8 explicam
restrições que o lima impunha e que deixaram de existir. **O que vale hoje está
nos tokens (`frontend/packages/ui/src/tokens.ts`), fixado por 28 testes em
`tokens.test.ts`, e nas decisões D9 e D10 no fim deste documento.**

## 1. Princípios

### 1.1 Mínimo, editorial, funcional

A interface deve parecer um produto desenhado com disciplina editorial, não um
template de admin.

Prefira: hierarquia clara, respiro generoso, poucas distrações visuais,
tipografia forte, separação tonal de superfície, espaçamento consistente, bordas
contidas, ícones simples.

Evite: gradientes decorativos, sombras em excesso, bordas desnecessárias, cores
em excesso, layouts densos, estética genérica de dashboard Bootstrap.

### 1.2 Uma voz cromática

Lima elétrico é o acento primário. Não introduza azul, roxo, vermelho, laranja ou
outra cor de ação saturada, a menos que a semântica exija — ação destrutiva ou
estado do sistema.

## 2. Tokens de cor

| Nome | Valor | Token | Uso |
|---|---|---|---|
| Electric Lime | `#beff50` | `--color-electric-lime` | CTA primário, acento ativo, superfície destacada |
| Off-Black Ink | `#14140f` | `--color-off-black-ink` | Texto principal, títulos, ícones |
| Off-White Canvas | `#f5f5eb` | `--color-off-white-canvas` | Superfícies secundárias, cartões, seções |
| Pure White | `#ffffff` | `--color-pure-white` | Canvas da página, conteúdo elevado |
| Ash | `#d2d2c8` | `--color-ash` | Bordas, divisores |
| Graphite | `#6e6e64` | `--color-graphite` | Texto secundário |
| Deep Charcoal | `#30302a` | `--color-deep-charcoal` | Seções escuras/invertidas raras |
| Stone | `#919183` | `--color-stone` | Traços estruturais tênues |
| Smoke | `#b9b9b7` | `--color-smoke` | Placeholder, lavagem sutil |

### Hierarquia de superfície

Use contraste tonal em vez de sombra:

1. `#ffffff` — canvas primário
2. `#f5f5eb` — superfície secundária / cartão
3. `#beff50` — superfície de acento / ação
4. `#30302a` — ilha escura rara

## 3. Tipografia

Preferida: `OTSono`. Fallback:
`Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif`

Pesos: **400** (corpo/apoio) e **500** (título, rótulo, CTA). Não use 600 nem 700
como parte do sistema.

| Papel | Tamanho | Entrelinha | Tracking |
|---|---|---|---|
| Eyebrow | 10px | 1.4 | 1px |
| Caption | 12px | 1.33 | 1.2px |
| Body Small | 14px | 1.29 | normal |
| Body | 16px | 1.5 | normal |
| Subheading | 22px | 1.18 | normal |
| Heading | 28px | 1.14 | -0.56px |
| Heading Large | 60px | 1 | -1.8px |
| Display | 90px | 0.89 | -2.7px |

Para dashboards, prefira a faixa **28px–40px** para títulos de página. Não use
tipografia display de 60px–90px a menos que a tela realmente se beneficie de
tratamento editorial.

Regras: título em peso 500; corpo em 400; texto de 28px para cima com tracking de
aproximadamente `-0.03em`; micro-rótulo em caixa alta com tracking de
aproximadamente `0.1em`; nunca preto puro em texto corrido; sem variação
excessiva de peso.

## 4. Espaçamento

Unidade base `4px`. Escala: `4, 8, 12, 16, 20, 24, 32, 40, 60, 64, 72, 80, 96`.

- Largura máxima da página: ~`1200px`
- Padding do cartão: `32px–48px`
- Gap entre elementos: `16px–24px`
- Gap entre seções: `80px–120px` em layout editorial
- Espaçamento de seção de dashboard pode ser reduzido quando a densidade de
  informação exigir

## 5. Raio de borda

| Elemento | Raio |
|---|---|
| Cartões | 28px |
| Superfícies internas | 18px |
| Botões | 28px |
| Pílulas/etiquetas | 9999px |
| Campos | 8px |

Consistência importa mais do que variedade de raios.

## 6. Elevação

**Sem sombra por padrão.** A hierarquia vem de cor de fundo, contraste de
superfície, espaçamento, tipografia e borda quando necessário.

Exceção: menu flutuante ou modal, onde a separação visual é genuinamente
necessária — e ainda assim sutil.

## 7. Botões

**Primário** — fundo `#beff50`, texto `#14140f`, raio 28px ou pílula, peso 500,
sem gradiente, sem sombra pesada.

**Secundário** — fundo transparente, texto `#14140f` ou `#6e6e64`, borda mínima
ou ausente, sublinhado ou afordância estrutural sutil. Não crie uma segunda cor
saturada de botão.

**Destrutivo** — vermelho semântico apenas quando necessário para usabilidade.
Vermelho não vira cor de marca.

## 8. Cartões

**Padrão** — fundo `#ffffff` ou `#f5f5eb`, raio 28px, padding 32px–48px, sem
sombra, borda opcional de 1px em `#d2d2c8`.

**De métrica** — valor grande, rótulo curto de apoio, informação secundária
contida, espaçamento interno generoso. Não encha todo cartão de ícone e
decoração.

**De lista** — linhas limpas com avatar/ícone, informação primária, informação
secundária e metadado/ação alinhados à direita. Raio 18px nas superfícies
internas.

## 9. Formulários

Padrão: fundo `#ffffff`, borda `1px solid #d2d2c8`, raio `8px`, texto `#14140f`,
placeholder `#6e6e64`, tamanho `16px`.

Rótulos sempre associados ao controle. Prefira rótulo visível a placeholder como
rótulo. Estado de foco claramente visível e acessível.

## 10. Navegação

**Sidebar** — preserve quando faz parte da arquitetura de informação; mantenha
mínima; superfícies brancas ou pergaminho; sem fundo colorido pesado; item ativo
pode usar superfície/acento lima sutil; ícones simples e monocromáticos; sem
excesso de etiqueta e decoração.

**Cabeçalho** — título claro, descrição contextual opcional, ações contidas,
espaçamento generoso. Navegação de aplicação não vira navegação de site de
marketing.

## 11. Layout de dashboard

```
Sidebar
   |
   +-- Cabeçalho da página
   |
   +-- Métricas de resumo
   |
   +-- Contexto / papel
   |
   +-- Conteúdo principal
   |
   +-- Ação primária
```

A arquitetura de informação exata segue os requisitos de negócio da aplicação.

## 12. Responsividade

**Desktop** — sidebar persistente, conteúdo em largura máxima confortável,
métricas em duas colunas quando útil, espaçamento horizontal generoso.

**Tablet** — menos padding, hierarquia preservada, cartões de métrica podem
quebrar, sem regiões vazias largas demais.

**Mobile** — sidebar vira gaveta, navegação inferior ou outro padrão móvel
existente; cartões em coluna única; botões podem ocupar a largura; tipografia
reduz proporcionalmente; alvos de toque preservados; sem rolagem horizontal.

Nunca resolva responsividade encolhendo o conteúdo de desktop.

## 13. Ícones

Simples, monocromáticos, visualmente consistentes, ~16–24px em UI normal. Não
misture bibliotecas de ícone sem motivo claro. Ícone apoia significado, não
decora todo componente.

## 14. Tabelas e listas

Priorize legibilidade: cabeçalhos limpos, bordas contidas, altura de linha
adequada, alinhamento claro, texto secundário em Graphite, sem zebra pesada a
menos que necessário. Para coleções pequenas, uma lista pode ser visualmente
superior a uma tabela.

## 15. Cores de estado

O sistema é intencionalmente quase monocromático, mas a semântica da aplicação
exige comunicar estado: sucesso, aviso, erro, informação. Cores semânticas são
secundárias à paleta de marca e nunca se tornam a linguagem visual dominante.

## 16. Acessibilidade

Preserve: navegação por teclado, foco visível, HTML semântico, rótulos
acessíveis, hierarquia de títulos, contraste suficiente, texto de botão
significativo, controles de formulário acessíveis, alvos de toque responsivos.

Nunca sacrifique acessibilidade para reproduzir uma captura de tela.

## 17. Faça / não faça

**Faça** — `#beff50` como ação visual primária; `#14140f` no texto principal;
`#6e6e64` no secundário; `#f5f5eb` e `#ffffff` nas superfícies; raio de 28px em
cartão; botão em pílula onde couber; respiro generoso; contraste tonal em vez de
sombra; linguagem visual contida; tokens reaproveitados entre componentes.

**Não faça** — gradientes; roxo/azul como cor de ação genérica; sombra pesada;
preto puro em texto normal; raios arbitrários; excesso de pesos; tudo virar
cartão; decoração sem função; layout de marketing dentro de aplicação
administrativa; mudar comportamento de negócio para alcançar semelhança visual.

## 18. Prioridade quando as decisões conflitam

1. Acessibilidade
2. Funcionalidade de negócio existente
3. Hierarquia de informação
4. Layout e espaçamento
5. Tipografia
6. Cor
7. Detalhe decorativo

---

# Aplicação ao Congrega — decisões e desvios

Esta seção registra o que foi decidido onde o documento acima deixou espaço, ou
onde ele conflitou com a aplicação. Cada item existe porque a decisão contrária
teria custado acessibilidade ou comportamento.

## D1 — Lima é cor de superfície, nunca de texto

`#beff50` sobre branco mede **1,19:1**. Como cor de texto ou de ícone é
ilegível — e como traço de estado de seleção também reprova o mínimo de 3:1 da
WCAG 1.4.11 para componente não textual.

O sistema anterior usava `colors.brand` (índigo) indistintamente como
preenchimento **e** como cor de texto de link. Trocar o valor para lima mantendo o
nome deixaria dezenas de textos invisíveis sem nenhum erro de compilação.

Por isso o token foi **renomeado** de `brand` para `surfaceAccent`: cada uso
antigo quebra o build e obriga a uma decisão explícita. Texto e ícone sobre lima
usam `textOnAccent` (`#14140f`, 15,5:1).

Link e ênfase textual, que antes eram índigo, agora são tinta principal com
sublinhado — exatamente o que a §7 pede para ação secundária ("sublinhado ou
afordância estrutural sutil"). A informação deixa de depender de cor.

## D2 — OTSono não existe no projeto; Inter em 400/500

`OTSono` é a fonte preferida do documento, mas não está disponível. O fallback
declarado é Inter, que o app já carrega via `@expo-google-fonts/inter`.

O peso 600 foi **removido** do carregamento e dos tokens: a §3 proíbe 600 e 700,
e manter a fonte carregada convidaria ao uso.

## D3 — Padding de cartão: 24px em linha de lista, 32px em painel

A §8 pede 32px–48px. A lista de membros usa `Card` como **linha** — centenas de
linhas com 32px de padding transformariam a lista num rolamento infinito.

A §4 autoriza: "espaçamento de seção de dashboard pode ser reduzido quando a
densidade de informação exigir". Linha de lista fica em 24px (o mínimo que o raio
de 28px comporta sem a curva comer o conteúdo); cartão de métrica e painel ficam
em 32px.

## D4 — Canvas branco, cartão pergaminho

A §2 lista `#ffffff` como canvas primário e `#f5f5eb` como superfície de cartão,
e a §6 proíbe sombra. Com canvas e cartão ambos brancos, sobraria uma borda de
1px como única separação — frágil.

Cartão em pergaminho sobre canvas branco é a leitura que faz o "contraste tonal
em vez de sombra" da §6 funcionar de fato. Superfície interna ao cartão (o chip
de valor, a linha de aniversariante) volta ao branco, com raio de 18px — é o
tratamento visível na referência.

## D5 — `surfaceAccentSoft`, o único valor derivado

A §10 permite "superfície/acento lima sutil" no item ativo da navegação. Lima
cheio ali competiria com o botão primário, e o documento não define uma versão
diluída.

`#eefbd5` é o lima diluído sobre pergaminho. É o **único** valor de cor fora da
tabela da §2, e existe apenas para esse papel.

## D6 — Estado de seleção usa preenchimento, não só borda

Chips de categoria (financeiro, importação) marcavam seleção com borda colorida +
cor de texto. Sob D1 a borda lima seria invisível.

Seleção passa a ser **preenchimento lima com tinta principal**, que é
inconfundível, mede 15,5:1 e não depende de percepção de cor. Como só um chip
fica selecionado por vez, o lima continua contido.

## D8 — Pano de fundo das telas anônimas inverte a hierarquia de superfície

`entrar` e `código` são as únicas telas do app sem sidebar e sem dado de
igreja — a primeira coisa que qualquer pessoa vê. A referência do cliente para
essas duas telas mostra um cartão branco flutuando sobre um fundo pergaminho
com blobs em lima e um traço curvo fino, no mesmo espírito do hero do site
Perk, mas adaptado para autenticação em vez de marketing.

Isso inverte a leitura da §2 de propósito: nas telas autenticadas o branco é o
canvas e o pergaminho é a superfície secundária (cartão); aqui o pergaminho
com acento é o "canvas" da marca — o primeiro contato — e o branco é o cartão
elevado que carrega a função (campo, botão). A hierarquia formal continua a
mesma (branco = elevado, pergaminho = base); só a ordem de leitura da página
muda, porque aqui a marca fala antes da função.

O pano de fundo (`AuthBackdrop`, em `apps/mobile/src/`) é desenho vetorial
próprio com `react-native-svg` — dois círculos e uma curva, **derivados
inteiramente de `palette.electricLime` por opacidade** (`fillOpacity`/
`strokeOpacity`), nunca um segundo tom de verde hardcoded. Introduzir uma cor
de fundo à parte romperia a regra da §1.2 ("uma voz cromática") logo na
primeira tela que o usuário vê. Não é reprodução pixel a pixel da referência
— é a mesma assinatura (blobs contidos nos cantos + traço solto) na paleta já
estabelecida.

`entrar` e `código` compartilham a mesma casca (`AuthCard`) para não haver
quebra visual entre uma tela e a próxima — o usuário passa de uma para a
outra em segundos, e um estilo mudando no meio do fluxo pareceria bug.

## D7 — Verde e vermelho semânticos foram mantidos

A §15 admite cor de estado. `#1A8245` e `#D33B2C` já têm contraste verificado em
`tokens.test.ts`; trocá-los por tons quentes sem mandato do documento custaria
contraste testado em troca de harmonia. Continuam restritos a traço e a delta
numérico, nunca a texto corrido.

## D9 — O lima virou verde, e isso soltou três restrições

**Revisa a D1. Não a apaga:** o raciocínio dela continua correto para o lima, e
é o que explica por que o token se chama `surfaceAccent` e não `brand`.

`#beff50` media **1,19:1** sobre branco. Toda a D1, a D6 e metade da D8 eram
consequências disso: link não podia ser acento (virou tinta sublinhada), seleção
não podia ser borda colorida (virou preenchimento), e nenhum ícone ou avatar
podia carregar a cor da marca.

O acento passou a ser `#2E7D46`, o verde do mockup de referência. As medidas:

| Par | Contraste | Serve para |
|---|---|---|
| `brandGreen` sobre branco | 5,07:1 | texto normal, ícone, traço |
| `brandGreen` sobre pergaminho | 4,63:1 | texto normal, no limite |
| `brandGreenDeep` sobre branco | 6,45:1 | texto que não quer depender da superfície |
| `brandGreenDeep` sobre `greenWash` | 5,74:1 | rótulo do item ativo de navegação |
| branco sobre `brandGreen` | 5,07:1 | texto dentro do botão primário |
| tinta sobre `brandGreen` | 4,3:1 | **reprova** — não use |

**A última linha é a inversão que importa.** No sistema lima, `textOnAccent` era
tinta porque branco media 1,4:1. No verde é o contrário: a tinta fica abaixo do
mínimo e o branco passa. Um token que trocasse de valor sem ninguém reler a
medida deixaria todo botão primário em 4,3:1 — reprovado, mas não a ponto de
alguém notar de olho, que é o pior tipo de regressão de acessibilidade.

Três coisas passaram a ser possíveis, e as três já estão em uso:

1. **Ícone na cor de acento** — o ícone de tipo de evento na linha da agenda.
2. **Avatar preenchido com iniciais legíveis** — o bloco de identidade da
   sidebar. Com lima seria um círculo cinza.
3. **Chip de seleção com borda de acento** — o seletor de tipo do formulário.
   A D6 continua valendo por um motivo próprio, e não mais por contraste: a
   seleção é **preenchimento**, porque preenchimento sobrevive a quem não
   distingue a borda do fundo. A borda virou reforço, não o sinal.

O `AuthBackdrop` teve as opacidades reduzidas de 0,35/0,6 para 0,18/0,35. O
verde é um tom escuro: manter os números daria um fundo com peso para competir
com o cartão de login. O que se preservou foi a claridade resultante, não o
valor — ver o comentário no próprio arquivo.

## D10 — Grove: o mockup, corrigido onde ele reprovava

**Substitui o sistema Perk por inteiro.** A referência é um mockup HTML de
dashboard, e o alvo declarado foi fidelidade a ele. Este registro existe para
separar o que veio do mockup do que precisou mudar — e por quê.

### O que veio direto

| Elemento | Grove |
|---|---|
| Navegação | **barra superior**, no lugar da sidebar de 240px |
| Canvas | `#F5F7F2` — verde-acinzentado, não branco |
| Cartão | branco, raio 22px, **com sombra** difusa (7%, 42px) |
| Estrutura do painel | herói + 3 métricas + 2 painéis + atalhos + rodapé |
| Hierarquia | por **peso** tipográfico (400 a 800), não por tamanho |

A sidebar saiu por medida, não por gosto: 240px permanentes para cinco links que
ninguém relê, e era ela que espremia a grade de três cartões em telas de 1280px.

A sombra volta depois de o Perk a proibir, e a inversão é coerente: no Perk a
hierarquia vinha do contraste entre canvas branco e cartão pergaminho. No Grove
o cartão é branco e o canvas é o tom — e branco sobre verde-acinzentado claro
tem diferença tonal pequena demais para separar sozinha.

### O que precisou mudar, e o quanto

**O mockup tem 18 pares que reprovam o mínimo de 4,5:1 da WCAG.** Cada tom foi
escurecido pelo mínimo necessário; a diferença é imperceptível lado a lado.

| Token | Mockup | Grove | Por quê |
|---|---|---|---|
| Verde de ação | `#4c8b22` (4,18) | `#44831A` (4,66) | falhava como texto **e** como fundo de texto branco |
| Âmbar | `#c27a12` (3,45) | `#A45C00` (5,11 / 4,65 na lavagem) | falhava como texto de chip |
| Cinza de apoio | 8 tons, 2,45 a 3,90 | `#68716B` (4,68) | ver abaixo |
| Rótulo de seção | 9px, 2,66:1 | 10px, 4,92:1 | tamanho pequeno com contraste baixo é a pior combinação |

**Os oito cinzas viraram um.** O mockup tem `#758078`, `#7c857f`, `#7d8780`,
`#8a938d`, `#8b948e`, `#8d958f`, `#98a19a` e `#9aa19c` em papéis equivalentes —
diferem por 2% e reprovam todos. A variação é acidental, não projetada, e
esconde que nenhum deles era legível.

**O fio de borda continua em 1,21:1, e isso está certo.** A WCAG 1.4.11 exige
3:1 de *indicadores*; um fio que só separa superfícies não carrega informação, e
engrossá-lo desenharia uma caixa a caneta em volta de cada cartão. Há um teste
que fixa essa decisão em vez de deixá-la implícita.

### Âmbar é categórico, nunca ação

O Perk reservava uma cor cromática só (D1). O Grove admite duas, com uma regra
que evita o arco-íris: **verde diz o que se pode fazer, âmbar diz sobre o que é.**
Um botão âmbar quebra a regra e faz o usuário procurar a ação no lugar errado.

**Verde e âmbar têm luminância quase idêntica — 1,01:1 entre eles.** Quem não
distingue matiz vê os dois como o mesmo tom. Isto não é defeito a corrigir:
separá-los exigiria clarear o âmbar até o amarelo ou escurecer o verde até o
musgo, e o desenho se perderia. É **restrição a respeitar**, e há um teste que a
mantém visível: enquanto ele passar, categoria nunca pode ser comunicada só por
cor. Todo cartão categórico carrega ícone **e** rótulo escrito.

### Onde o Grove diverge do mockup de propósito

**A etiqueta de tipo de evento é de uma cor só.** O mockup pinta culto de verde,
reunião de âmbar e estudo de roxo. Aqui os tipos são cadastrados pela igreja:
ela pode ter dois ou quinze, e uma paleta fixa de três teria de repetir cores ou
inventar tons a cada tipo novo. O nome escrito distingue sem depender de cor.

**Não existe "↑ 12,5% vs. mês anterior".** O mockup traz esse delta no cartão de
membros. Nada guarda a contagem de meses passados, e inventar a variação daria
ao usuário um número em que ele confiaria para decidir. Os rodapés que ficaram —
próximo aniversário, divisão dos eventos por tipo — saem de dados reais.
