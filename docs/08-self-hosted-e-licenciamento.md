# 08 — Self-hosted com licença central

Mudança de direção: o ChMS passa a rodar na infraestrutura do cliente, e a
Congrega mantém apenas o servidor de licenças e o acervo de conteúdo premium.

Este documento entrega os quatro artefatos pedidos e registra **o que a mudança
invalida** nas decisões anteriores — porque contrariar um ADR sem revisá-lo é o
que produz retrabalho estrutural (ver `CLAUDE.md`).

---

## 0. O que a mudança melhora, e o que ela quebra

### Melhora — e este é o ganho principal, maior que o de custo

**A Congrega deixa de ser operadora de dados pessoais.** A premissa P4 dizia que
a igreja é controladora e a Congrega é operadora dos dados dos membros. No modelo
self-hosted a Congrega **não toca** nesses dados: eles nascem, vivem e morrem no
banco do cliente.

O que sobra sob nossa responsabilidade é o cadastro de licença e cobrança — dados
nossos, de titulares que contrataram conosco. Um vazamento nosso deixa de expor
ficha de criança, endereço de membro e histórico de dízimo de centenas de
igrejas.

**Isto precisa entrar em P4 e no contrato.** É argumento comercial e é redução
material de risco.

### Simplifica

P3 e o ADR-021 giram em torno de uma restrição do Supabase: o pooler em modo
*transaction* devolve a conexão a cada transação, o que quebra
`pg_advisory_lock` de sessão. Numa instalação self-hosted **não há pooler entre a
aplicação e o Postgres**, e a restrição desaparece — a conexão direta é a única
que existe.

O par `PooledConnectionString` / `DirectConnectionString` **fica**, apontando para
o mesmo lugar. Removê-lo agora fecharia a porta para o cliente que um dia colocar
um PgBouncer na frente.

### Quebra — e precisa de decisão

> **⚠️ Supabase Storage para vídeo contraria o ADR-010, com números.**

O ADR-010 rejeitou Supabase Storage para o catálogo pesado e escolheu R2 por uma
razão quantificada:

> *um curso com 20 aulas de 500 MB, assistido por 10.000 assinantes, são 100 TB
> de egress. A US$ 0,085/GB … ~US$ 8.500 em um único curso. Se a assinatura custa
> R$ 29,90/mês, o conteúdo consome a receita antes de qualquer outro custo.*

O Supabase Storage cobra egress na mesma ordem de grandeza. O R2 cobra **zero**.
A conta não mudou porque a aplicação virou self-hosted — na verdade **piorou**,
porque agora o egress é o nosso único custo variável, sem a receita do ChMS
diluindo.

**Recomendação:**

| Conteúdo | Onde | Por quê |
|---|---|---|
| Vídeo (aulas) | Bunny Stream ou Mux | HLS + token de playback; egress embutido no preço |
| eBooks, PSD, projetos | **Cloudflare R2** | egress zero; é o item que decide a viabilidade |
| Thumbnails, capas, avatares | Supabase Storage | leves, cacheáveis, egress irrelevante |

O fluxo desenhado na seção 2 **não muda** com a escolha de fornecedor — só muda
qual SDK assina a URL. Se a decisão for manter tudo no Supabase Storage, que seja
com a conta feita e o teto de egress monitorado, não por omissão.

### Quebra — e ninguém perguntou ainda

**Distribuição de versão.** Hoje publicamos e todo mundo está na versão nova.
Self-hosted significa que existirão igrejas rodando a versão de dois anos atrás,
com o bug que já corrigimos, pedindo suporte. Três consequências imediatas:

1. **Migrations precisam ser idempotentes e para frente apenas.** Já são.
2. **A API central precisa versionar o contrato de licença** e tolerar clientes
   antigos por bastante tempo.
3. **O servidor central sabe qual versão cada instalação roda** — o cabeçalho da
   consulta de licença é o único canal que temos. Vale usá-lo desde o primeiro
   dia, mesmo sem nada consumindo ainda.

---

## 1. Estratégia de variáveis de ambiente

O arquivo pronto está em **`.env.example`**, na raiz. Aqui ficam as decisões que
o guiaram.

### A regra: dois sublinhados, nunca dois-pontos

```
appsettings.json            variável de ambiente
Database:PooledConnection   Database__PooledConnectionString
```

Dois-pontos funciona no Linux e falha em contêiner Windows e em vários
orquestradores. Dois sublinhados funciona em todos. **É a causa número um de
"configurei e a aplicação ignora"** — está escrito no topo do `.env.example`
porque é a primeira coisa que o suporte vai perguntar.

### Três classes de configuração, com regras diferentes

| Classe | Exemplo | Regra |
|---|---|---|
| **Infraestrutura do cliente** | conexão do banco, porta | do `.env`, obrigatório, sem padrão |
| **Segredos gerados por instalação** | `SigningKeyPem`, as duas `DataKey` | do `.env`, **a aplicação não sobe sem** |
| **Ajuste operacional** | intervalo do Outbox, fuso | do `.env`, **com padrão sensato** |

A segunda classe usa `[Required]` com `ValidateOnStart` — já é o padrão do
código. Subir com a cifragem desligada gravaria ficha médica de criança em texto
claro sem nada acusar; **não subir é a falha preferível**, e é a premissa P8
aplicada.

### O aviso que precisa estar em caixa alta

`ChildSafety__DataKey` e `Connectors__DataKey` cifram dados **que já estão no
banco do cliente**. Perdê-las é perder o conteúdo cifrado — e agora **não existe
suporte possível**, porque a Congrega nunca teve as chaves.

No modelo hospedado, um cliente que perdesse a chave ligava para nós. Aqui não
há para quem ligar. O `.env.example` diz isso com o alerta antes das variáveis,
não depois.

### O que saiu

`Payments__*` **deixa a instalação do cliente.** A cobrança acontece na nossa
infraestrutura; a instalação do cliente nunca fala com o Abacate.pay, nunca
recebe webhook de pagamento e nunca vê dado de cartão. Manter as variáveis lá
seria manter uma superfície que não é mais usada — e um tutorial antigo faria
alguém preenchê-las achando que precisa.

---

## 2. Fluxo *phone home*

### Duas conversas distintas, e confundi-las é o erro

```
  ┌─────────────────┐        ┌──────────────────────┐        ┌─────────────┐
  │  App do membro  │        │  Servidor da igreja  │        │  Congrega   │
  │  (React Native) │        │  (self-hosted)       │        │  (central)  │
  └────────┬────────┘        └──────────┬───────────┘        └──────┬──────┘
           │                            │                           │
      (A)  │  quem é você?              │                           │
           │  ── JWT local ──────────►  │                           │
           │                            │                           │
      (B)  │  quero a aula 12           │                           │
           │  ─────────────────────►    │                           │
           │                            │  licença ainda vale?      │
           │                            │  (cache, 6h) ──────────►  │
           │                            │                           │
           │                            │  ◄── veredito ASSINADO ── │
           │                            │                           │
           │                            │  emita URL da aula 12     │
           │                            │  + chave + id da          │
           │                            │    instalação ─────────►  │
           │                            │                           │  valida
           │                            │                           │  licença,
           │                            │                           │  mede uso,
           │                            │                           │  assina
           │                            │  ◄── URL assinada, 5 min ─│
           │  ◄── URL assinada ──────   │                           │
           │                            │                           │
      (C)  │  ─────── baixa direto do storage, sem passar por ninguém ──────►
```

**(A) A identidade do membro é do cliente.** A Congrega não conhece os membros da
igreja e não deve conhecer — é o ganho de LGPD da seção 0. Quem autentica o
membro é o servidor local, com o JWT que ele já emite.

**(B) A licença é da instalação.** A Congrega não valida o membro; valida a
igreja. Isso move a fronteira de confiança, e a seção 3 trata da consequência.

**(C) O arquivo nunca passa por nós nem por eles.** Nem a API central nem o
servidor da igreja fazem proxy do vídeo. É o mesmo princípio do ADR-010 — a API
autoriza e delega — e aqui ele também protege a banda do cliente.

### Por que dois passos e não um

Poderíamos emitir a URL direto, sem a consulta de licença separada. Não fazemos
porque as duas perguntas têm ritmos diferentes:

- **"a licença vale?"** muda uma vez por mês. Cache de 6 h.
- **"me dê a URL da aula 12"** acontece a cada play, e precisa de TTL curto,
  medição e registro.

Juntar as duas obrigaria a ir à rede a cada play para responder algo que muda
mensalmente — e é exatamente o desenho que derruba o servidor central às 19h de
domingo.

### Contrato da consulta de licença

```http
GET https://licenca.congrega.app/v1/license/status
X-Congrega-License: cong_live_a1b2c3...
X-Congrega-Instance: 8f4e1c...
X-Congrega-Version: 1.4.2
```

```json
{
  "payload": "eyJ2YWxpZCI6dHJ1ZSwibGljZW5zZUtleSI6ImNvbmdfbGl2ZV8uLi4i...",
  "signature": "Yk8fQ2..."
}
```

`payload` é Base64URL de um JSON com `valid`, `licenseKey`, `reason` e
`validUntil`; `signature` é RSA-SHA256 sobre ele. **A assinatura é o que impede o
cliente de responder a si mesmo** — um `hosts` apontando nosso domínio para
`localhost` faz qualquer um devolver `{"valid":true}`, mas ninguém devolve um
`{"valid":true}` **assinado** sem a chave privada.

### Contrato da emissão de URL

```http
POST https://licenca.congrega.app/v1/content/8f2a/url
X-Congrega-License: cong_live_a1b2c3...
X-Congrega-Instance: 8f4e1c...

{ "memberRef": "sha256:9c1f...", "purpose": "stream" }
```

`memberRef` é um **hash** do identificador do membro, com sal da instalação — não
o e-mail, não o nome. Ele serve para dois propósitos e nenhum deles exige saber
quem é a pessoa: contar assinantes distintos para a medição, e carimbar a marca
d'água. Mandar o e-mail cru anularia o ganho de LGPD da seção 0 pela porta dos
fundos.

---

## 3. Segurança contra fraude

### O que precisa ser dito antes de qualquer técnica

> **Você não protege um binário que entrega ao adversário.**

O cliente tem o contêiner, o banco, o `.env` e — se o projeto for aberto ou
vazar — o código. Qualquer verificação que rode **na infraestrutura dele** é
removível: apagar a linha do `Program.cs`, trocar o `LicenseGuard` por um
`return Valid = true`, ou apontar `Licensing__CentralUrl` para um servidor
próprio.

Ofuscação, checksum do binário, "anti-debug": elevam o custo em horas e caem no
primeiro adversário motivado. Nos termos que este projeto já usa para DRM,
seriam **security theater**.

**A pergunta certa não é "como impedir que ele burle a checagem", é "o que ele
ganha burlando".** E a resposta, se a arquitetura estiver certa, é: **nada**.

### A fronteira real: o conteúdo não está lá

O arquivo não existe na infraestrutura do cliente. Burlar o `LicenseGuard` dá
acesso a uma tela que vai pedir uma URL ao nosso servidor — e o **nosso** servidor
valida a licença antes de assinar. Ele não roda na máquina dele, não tem o código
dele e não aceita ordens dele.

Isto reduz todo o problema a **uma** regra, e é ela que precisa ser inviolável:

> **A URL assinada só é emitida pelo servidor central, e só com licença ativa.**

Tudo o mais nesta seção é medição e resposta.

### As fraudes que sobram, e o que fazer com cada uma

#### 1. Compartilhar a chave entre igrejas

Uma igreja assina e passa a chave para outras dez. É **a fraude mais provável**,
porque é a mais fácil e não exige nenhuma habilidade técnica.

- `X-Congrega-Instance` identifica a instalação. Uma licença de igreja única
  aparecendo em nove instalações é um sinal forte.
- Limite de assinantes distintos (`memberRef`) por licença, dimensionado pelo
  plano. Uma igreja de 80 membros com 900 `memberRef` distintos não é um erro de
  medição.
- Limite de emissão de URLs por licença por hora.
- **A resposta é comercial primeiro**: contato, upgrade de plano, e revogação
  como último passo. Uma igreja pega compartilhando é uma venda perdida se for
  bloqueada e uma venda maior se for abordada.

O identificador é forjável, e isso é esperado — ele serve para pegar o
compartilhamento **casual**, que é o caso comum, não o adversário dedicado.

#### 2. Vazar a URL assinada

Um membro copia o link e publica no WhatsApp.

- TTL curto — 5 min para stream, 60 s para download. O link morre antes de
  circular.
- **Marca d'água com o `memberRef`** no player e no rodapé do PDF. É o dissuasor
  mais eficaz por real investido, porque personaliza a responsabilidade — já está
  no ADR-010 como item de MVP.
- Token por segmento no HLS, para o `ffmpeg` ingênuo não funcionar. Fase 2.

#### 3. Responder a si mesmo

Apontar `licenca.congrega.app` para um servidor próprio via `hosts`.

- **Resolvido pela assinatura RSA.** A instalação verifica com a chave pública; o
  falsificador precisaria da privada.
- A chave pública vem embutida na imagem, com `Licensing__PublicKeyPem` para
  rotação. Trocar a chave pública é possível — mas quem faz isso já está editando
  a instalação, e cai no caso "não ganha nada".

#### 4. Recuar o relógio

Uma licença vencida volta a valer se o servidor tiver a data para trás, e num
servidor próprio isso é um comando.

- `LicenseGuard` guarda o maior instante já visto e nunca aceita leitura
  anterior. Custa uma comparação.
- **Pendência conhecida:** hoje esse instante vive em memória e zera no reinício.
  Fechar exige uma linha na tabela de configuração do banco local. Enquanto não
  fechar, reiniciar o contêiner apaga a proteção — e isso está escrito no
  `LicenseGuard`, não escondido.
- A defesa que não depende do relógio do cliente é o servidor central: o
  `validUntil` que ele assina é medido **pelo relógio dele**.

#### 5. Reproduzir um veredito antigo

Capturar a resposta assinada de uma igreja adimplente e devolvê-la sempre.

- O veredito carrega `licenseKey`, e a instalação **compara com a própria**. Um
  veredito assinado para outra licença é recusado.
- `validUntil` limita a janela de reprodução.
- Para fechar de vez seria preciso um *nonce* por consulta. Não está feito, e a
  razão é o custo/benefício: quem consegue capturar e reproduzir já consegue
  editar o binário — e volta a "não ganha nada".

### O que aceitamos, explicitamente

| Risco | Decisão |
|---|---|
| Cliente edita o código e remove a checagem local | **Aceito.** Não ganha acesso a conteúdo nenhum |
| Assinante grava a tela e redistribui | **Aceito.** Detecção e suspensão, não prevenção (ADR-010) |
| Revogação leva até 7 dias numa instalação sem rede | **Aceito.** É o preço de não sermos ponto único de falha |
| Identificador de instalação forjável | **Aceito.** Serve para medir o casual, não para conter o dedicado |

---

## 4. O código

Três arquivos, **compilando no repositório** e ainda **não ligados** ao pipeline —
a ligação depende do servidor central existir.

| Arquivo | Papel |
|---|---|
| `src/Congrega.Infrastructure/Licensing/LicenseOptions.cs` | configuração da licença |
| `src/Congrega.Infrastructure/Licensing/LicenseGuard.cs` | consulta, cache, chamada única, tolerância |
| `src/Congrega.Infrastructure/Licensing/LicenseKeyHandler.cs` | `DelegatingHandler` que anexa a chave |
| `src/Congrega.Api/Middleware/PremiumContentGuardMiddleware.cs` | barra as rotas premium |

### Middleware **e** `DelegatingHandler` — a divisão não é estilo

Middleware intercepta o que **entra** no servidor do cliente: é onde a requisição
do membro é barrada. `DelegatingHandler` intercepta o que **sai**: é onde a
credencial é anexada às nossas chamadas. Trocar os dois de lugar significaria
carimbar a chave de licença em requisições de membros.

### Por que **não** `IMemoryCache`

O pedido foi "fazer cache para não derrubar nosso servidor". `IMemoryCache`
resolve o *guardar* e não resolve os dois problemas que aparecem de fato:

**Debandada no cache frio.** Cinquenta membros abrindo o app às 19h de domingo
produzem cinquenta requisições simultâneas: o padrão "verifica, não achou, busca"
não tem exclusão mútua entre as verificações. O `SemaphoreSlim` com **segunda
verificação sob a trava** colapsa as cinquenta numa. Sem a segunda verificação, a
trava apenas serializa a debandada — não a evita.

**O último veredito bom precisa sobreviver à expiração.** Uma entrada de cache
expira e some. Aqui a resposta antiga é justamente o que sustenta a janela de
tolerância quando o nosso servidor cai. Um cache que apaga o que expirou destrói
o dado que mais importa no pior momento.

### A distinção que evita o incidente

```csharp
if (resposta.StatusCode is Forbidden or PaymentRequired)
{
    // Recusa EXPLÍCITA: descarta o veredito velho na hora.
}
// Qualquer outra falha → tolerância, com o último veredito bom.
```

Um 403 é uma resposta: a assinatura venceu, e a tolerância **não** se aplica. Um
timeout é ausência de resposta: a tolerância se aplica. Tratar os dois igual faria
uma queda nossa cortar o conteúdo de todo mundo — ou uma licença revogada
continuar valendo uma semana por engano.

### O que o middleware **não** guarda

Se a assinatura do Congrega+ vencer, a igreja continua entrando no sistema, vendo
os membros, lançando o dízimo e imprimindo a agenda. Só o catálogo — que é nosso
e mora conosco — depende da assinatura.

Travar o ChMS por falta de pagamento seria tomar como refém um dado do qual a
igreja é a **controladora**, rodando num servidor **dela**. Além de errado como
produto, é frágil como posição jurídica.

A lista de rotas protegidas é de **inclusão**, não de exclusão: com exclusão,
toda tela nova nasceria protegida, e a próxima tela de membros ficaria
inacessível para quem não assina o Congrega+ sem ninguém perceber até o suporte
tocar.

---

## 5. O que falta antes de ligar isto

| Item | Por quê |
|---|---|
| **O servidor central não existe** | é o próximo entregável: emissão de licença, webhook do Abacate.pay, assinatura do veredito e emissão de URL |
| Persistir o maior instante visto | sem isso, reiniciar o contêiner apaga a defesa contra relógio recuado |
| Persistir o id da instalação | hoje muda a cada reinício, e cada reinício parece instalação nova na medição |
| Par de chaves de licenciamento | gerar, guardar a privada no nosso secret manager e embutir a pública na imagem |
| Decidir o storage do vídeo | ver a seção 0 — a conta de egress é a que decide a viabilidade |
| Atualizar P3, P4 e ADR-010 | a mudança invalida parte dos três, e ADR desatualizado gera retrabalho |
| `Dockerfile` e `docker-compose.yml` de distribuição | o que existe hoje é de desenvolvimento |
