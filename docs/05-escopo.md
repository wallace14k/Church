# Congrega — Corte de Escopo

> Seção 4 do briefing. Cada item funcional classificado em MVP / Fase 2 / Fase 3,
> com a justificativa do corte.

---

## A recomendação principal: não lance os dois produtos juntos

O briefing descreve dois produtos com modelos de receita distintos e trata a dualidade
como requisito de primeira classe. **Isso é verdade para a arquitetura, mas não deveria ser
para o lançamento.**

Construir o ChMS completo e o Hub Premium completo em paralelo, com a equipe da premissa P7,
significa entregar dois produtos medianos e nenhum que alguém queira pagar. Minha recomendação:

> **MVP = ChMS enxuto + a fundação completa de billing e entitlements.
> Congrega+ entra na Fase 2, sobre trilhos já prontos.**

Três razões, em ordem de peso:

1. **Distribuição.** O ChMS é o que transforma a igreja em cliente pagante e coloca o app na
   mão de cada membro. Lançar o hub de conteúdo primeiro é competir por atenção sem nenhuma
   vantagem de distribuição — contra YouTube e contra todo pastor que já publica de graça.
   Com 300 igrejas usando o ChMS, o Congrega+ nasce com uma base cativa e custo de aquisição
   próximo de zero.
2. **A fundação cara é a mesma.** Assinaturas, pagamentos, webhooks idempotentes,
   entitlements e o motor de retenção precisam existir para vender o ChMS. Uma vez prontos,
   acrescentar o produto B2C é trabalho de catálogo e paywall — não de arquitetura.
   **Retrofitar entitlements depois seria caríssimo; construí-los desde já custa pouco.**
3. **Risco regulatório concentrado.** O conflito com as regras de loja (ADR-009) só existe no
   produto B2C. Adiá-lo dá tempo de validar o enquadramento com um app que não vende conteúdo
   digital nenhum — e, portanto, tem superfície mínima de interpretação na revisão da App Store.

**O que isso não significa:** não significa modelar o banco só para o ChMS. `users` global,
`memberships`, `plans.audience` e `entitlements` entram no MVP exatamente como projetados. A
arquitetura suporta os dois produtos desde o primeiro commit; apenas a *interface* do segundo
não é construída ainda.

---

## Módulo Core — Gestão de Igrejas

| Item | Fase | Justificativa |
|---|---|---|
| Cadastro de membros e famílias | **MVP** | É o núcleo do produto. Sem ele não existe ChMS |
| Controle financeiro — lançamento, categorias | **MVP** | Segundo maior motivo de compra. Hoje a maioria usa planilha |
| Relatórios financeiros básicos | **MVP** | Fechamento mensal e por categoria. O suficiente para prestar contas |
| Relatórios avançados, orçado × realizado, DRE | Fase 2 | Vende bem em demo, é pouco usado no primeiro ano |
| Calendário de eventos | **MVP** | Barato de construir e pré-requisito do check-in |
| **Check-in infantil** com etiqueta e código de retirada | **MVP — em piloto** | Ver seção dedicada abaixo |
| Check-in de eventos adultos | Fase 2 | Valor bem menor que o infantil; nenhuma igreja troca de sistema por ele |
| Pequenos grupos e células com hierarquia de liderança | Fase 2 | Alta complexidade de modelagem (árvore de liderança, multiplicação, relatórios em cascata) para um módulo que só as igrejas maiores usam de fato |
| Comunicação em massa (push/e-mail para membros) | Fase 2 | Requer a infraestrutura de notificação, que o MVP já constrói para retenção |

### Sobre o check-in infantil

É o item de **maior risco e maior valor** do MVP, e a decisão merece ser explícita.

**Por que mantê-lo:** é o gatilho de compra número um em igrejas de médio porte. Segurança de
criança é a única funcionalidade pela qual uma igreja troca de sistema no meio do ano. E é o
que faz **cada pai instalar o app** — que é, não por acaso, o canal de distribuição do
Congrega+ na Fase 2.

**Por que ele é perigoso:** concentra dado de criança (LGPD Art. 14), foto, alergia e
responsável autorizado. Um incidente aqui não é multa — é o fim da marca.

**Recomendação: entra no MVP, mas como o último item a ser liberado, e em piloto fechado com
3 a 5 igrejas parceiras.** O piloto não é cerimônia: é o que permite descobrir na prática que
a fila do berçário tem 40 pais simultâneos com Wi-Fi ruim, antes que isso aconteça em 300
igrejas ao mesmo tempo.

**Portões obrigatórios antes de qualquer liberação, sem exceção:**

- criptografia em nível de aplicação para alergias, foto e observações;
- `public_id` opaco na etiqueta impressa e em toda URL (D1);
- código de retirada hasheado, uso único e com TTL;
- log de auditoria em toda leitura de ficha;
- fluxo de consentimento parental com registro de prova;
- fila offline funcionando com Wi-Fi ruim — requisito real, não desejável;
- **parecer jurídico sobre o enquadramento do Art. 14.**

Se algum desses não estiver pronto, o item sai do MVP. Nenhum deles é negociável por prazo.

---

## Módulo Educação e Teologia

| Item | Fase | Justificativa |
|---|---|---|
| Biblioteca de eBooks (PDF/EPUB) com entrega assinada | **Fase 2** | O caminho mais barato para validar disposição a pagar: sem transcodificação, sem HLS, sem custo de egress relevante |
| Plataforma de vídeo com trilhas e progresso | Fase 2 | Núcleo do Congrega+. Depende do provedor de vídeo e do paywall por plataforma |
| Estudos bíblicos arqueológicos e históricos | Fase 2 | É produção de **conteúdo**, não de software. O gargalo é editorial, e ele deve começar em paralelo ao MVP |
| Certificados de conclusão | Fase 3 | Ótimo para retenção, irrelevante para aquisição. Só faz sentido com trilhas maduras |

> **Observação de negócio, fora do escopo técnico:** o Congrega+ é um negócio de catálogo. Sem
> um pipeline editorial rodando, a plataforma entrega uma prateleira vazia. Recomendo iniciar a
> produção de conteúdo **junto com o MVP**, não depois — o software fica pronto antes do
> catálogo, e não o contrário.

---

## Módulo Packs Premium

**Resposta à pergunta do briefing:** o pack é **compra avulsa E incluso na assinatura**, e essa
dualidade não custa nada porque o modelo já a suporta. `resource_packs` descreve o conteúdo;
`plans.price_cents` e `resource_packs.price_cents` descrevem as formas de venda; `entitlements`
registra como cada usuário obteve acesso. O mesmo pack pode ser vendido avulso por R$ 49,
incluído no plano anual e dado de cortesia — sem duplicar linha e sem `if` no caminho de
autorização (ver `docs/04-modelagem-dados.md` §2.2).

| Item | Fase | Justificativa |
|---|---|---|
| Sermões e materiais de campanha | **Fase 2** | Menor esforço de produção, maior giro |
| Artes editáveis (PSD/AI) | Fase 2 | Arquivo grande, mas download simples — sem streaming |
| Mídia de fundo | Fase 3 | Concorrência gratuita abundante |
| Projetos de design de ambiente | Fase 3 | Nicho pequeno |
| Projetos de som e iluminação | Fase 3 | Nicho ainda menor; alta complexidade de suporte |

---

## Módulo Monetização

| Item | Fase | Justificativa |
|---|---|---|
| Assinatura do ChMS via Abacate.pay (web) | **MVP** | É a receita do MVP. Sem isso não há produto |
| Máquina de estados da assinatura + webhooks idempotentes | **MVP** | Fundação. Retrofitar idempotência depois de perder dinheiro é o pior momento para aprender |
| Entitlements | **MVP** | Mesmo sem conteúdo premium, é o que resolve acesso a módulos do ChMS por plano |
| **Motor de retenção** (D-15/D-7/D-3/D-1/D+3) | **MVP** | Ver abaixo |
| Cupom e trial | Fase 2 | Necessário para campanha de aquisição, não para o primeiro cliente |
| Paywall granular por plataforma + IAP | Fase 2 | Chega junto com o Congrega+ |
| Assinatura anual com desconto | Fase 2 | Melhora fluxo de caixa; exige política de reembolso definida antes |

### Por que o motor de retenção está no MVP

É contraintuitivo colocar retenção antes de ter o que reter, mas a lógica é de custo, não de
valor imediato: **o churn do primeiro ano é o que decide se o negócio existe no segundo**, e
uma igreja que perde acesso por cartão vencido sem nenhum aviso não volta — ela volta para a
planilha. O worker do entregável 6.5 são poucas centenas de linhas sobre infraestrutura que já
precisa existir (fila, outbox, jobs). O retorno é desproporcional ao custo.

---

## Resumo do MVP

**Dentro:** cadastro de membros e famílias · financeiro com lançamentos, categorias e relatório
básico · calendário · check-in infantil em piloto com portões de segurança · autenticação OTP
com RBAC + policy · assinatura do ChMS via Abacate.pay · webhooks idempotentes · entitlements ·
motor de retenção · observabilidade e auditoria.

**Fora:** todo o Congrega+ · células · check-in adulto · IAP · cupons · relatórios avançados ·
packs.

**A régua para dizer não:** um item entra no MVP se, sem ele, uma igreja **não assina o
contrato**. Tudo que apenas torna o produto melhor para quem já assinou é Fase 2. Essa régua é
o que impede o MVP de virar a versão 1.0 completa com outro nome.
