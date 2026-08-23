-- Documento fiscal da despesa: a prova de que o dinheiro que saiu foi para onde
-- o lançamento diz.
--
-- ============================================================================
-- Três decisões que o schema toma, e o porquê de cada uma
-- ============================================================================
--
-- 1. **Documento em tabela própria, e o arquivo numa terceira.**
--
--    Os bytes do comprovante chegam a 10 MB. Se morassem na linha do
--    lançamento, toda listagem do mês arrastaria dezenas de megabytes para
--    desenhar uma coluna de valores — e ninguém perceberia, porque a tela
--    continuaria certa, só lenta. Separando, a listagem lê linhas de centenas
--    de bytes e o arquivo só é buscado quando alguém pede para vê-lo.
--
-- 2. **"Só saída tem documento fiscal" é constraint, não `if` na aplicação.**
--
--    Nota fiscal de uma ENTRADA não existe: a igreja não emite nota ao receber
--    dízimo. A regra vale mesmo para quem escrever direto no banco, então é a
--    FK composta abaixo que a sustenta — não uma verificação prévia que sob
--    concorrência tem janela.
--
-- 3. **O arquivo é BYTEA, e o Base64 é só o transporte.**
--
--    JSON não carrega binário; o contrato da API recebe e devolve Base64. Mas
--    persistir o Base64 *literal* custaria 33% a mais de disco em cada
--    comprovante e ainda exigiria decodificar a cada leitura. O banco guarda os
--    bytes; a borda faz a tradução.

-- ---------------------------------------------------------------------------
-- Pré-requisito da FK composta
-- ---------------------------------------------------------------------------
--
-- Uma FK precisa apontar para uma chave única. Para que o documento consiga
-- referenciar "o lançamento X, **que é uma saída**", o par (id, kind) precisa
-- ser declarado único — o que é trivialmente verdade, já que `id` sozinho já é
-- a PK. A unicidade redundante existe só para dar à FK um alvo válido.
--
-- É a técnica padrão para exprimir invariante entre tabelas de forma
-- declarativa. A alternativa seria um TRIGGER, que esconde a regra num lugar
-- onde ninguém lendo o `\d` da tabela a encontraria.
ALTER TABLE giving_entries
    ADD CONSTRAINT uq_giving_entries_id_kind UNIQUE (id, kind);

-- ---------------------------------------------------------------------------
-- Documento fiscal
-- ---------------------------------------------------------------------------
CREATE TABLE giving_documents (
    id              BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id       UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id       BIGINT       NOT NULL,

    entry_id        BIGINT       NOT NULL,

    -- Redundante com `giving_entries.kind` de propósito: é a coluna que a FK
    -- composta compara, e o CHECK abaixo a prende em "saída". Sem ela, a FK não
    -- teria como exprimir a regra.
    entry_kind      SMALLINT     NOT NULL,

    -- 1=NF-e 2=NFS-e 3=Cupom fiscal (CF-e/SAT) 4=Recibo
    doc_type        SMALLINT     NOT NULL,

    number          VARCHAR(60),
    series          VARCHAR(20),

    -- Só dígitos: 11 para CPF, 14 para CNPJ. A pontuação é da tela, e guardá-la
    -- faria "12.345.678/0001-90" e "12345678000190" serem emissores diferentes.
    issuer_tax_id   VARCHAR(14),

    -- Chave de acesso da NF-e/NFC-e: 44 dígitos, sempre.
    access_key      VARCHAR(44),

    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_giving_documents_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- **A regra "documento só em saída", declarada.**
    --
    -- `ON DELETE CASCADE` porque o documento não existe sem o lançamento: é a
    -- prova DELE. Apagar o lançamento e deixar a nota órfã produziria um
    -- comprovante que não comprova nada.
    CONSTRAINT fk_giving_documents_entry FOREIGN KEY (entry_id, entry_kind)
        REFERENCES giving_entries (id, kind) ON DELETE CASCADE,

    -- 2 = Saida. É o que fecha a regra: a FK garante que o par existe, e este
    -- CHECK garante que o par só pode ser de saída.
    CONSTRAINT ck_giving_documents_so_saida CHECK (entry_kind = 2),

    -- Um lançamento tem no máximo um documento fiscal. Dois comprovantes para a
    -- mesma despesa é o começo de uma despesa lançada em dobro.
    CONSTRAINT uq_giving_documents_entry UNIQUE (entry_id),

    CONSTRAINT uq_giving_documents_public_id UNIQUE (public_id),
    CONSTRAINT ck_giving_documents_tipo CHECK (doc_type BETWEEN 1 AND 4),

    -- Nota, nota de serviço e cupom SEMPRE têm número — é o que os identifica
    -- perante o fisco. Recibo (4) é o único que pode não ter: recibo escrito à
    -- mão é prova legítima e frequentemente não numerada.
    CONSTRAINT ck_giving_documents_numero
        CHECK (doc_type = 4 OR number IS NOT NULL),

    CONSTRAINT ck_giving_documents_emissor
        CHECK (issuer_tax_id IS NULL OR issuer_tax_id ~ '^[0-9]{11}$|^[0-9]{14}$'),

    CONSTRAINT ck_giving_documents_chave
        CHECK (access_key IS NULL OR access_key ~ '^[0-9]{44}$')
);

CREATE INDEX ix_giving_documents_tenant ON giving_documents (tenant_id);

ALTER TABLE giving_documents ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_giving_documents ON giving_documents
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- ---------------------------------------------------------------------------
-- O arquivo anexado
-- ---------------------------------------------------------------------------
CREATE TABLE giving_document_files (
    id            BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id     UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id     BIGINT       NOT NULL,

    document_id   BIGINT       NOT NULL,

    file_name     VARCHAR(255) NOT NULL,
    content_type  VARCHAR(100) NOT NULL,
    size_bytes    INTEGER      NOT NULL,
    content       BYTEA        NOT NULL,

    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_giving_document_files_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    CONSTRAINT fk_giving_document_files_document FOREIGN KEY (document_id)
        REFERENCES giving_documents (id) ON DELETE CASCADE,

    CONSTRAINT uq_giving_document_files_document UNIQUE (document_id),
    CONSTRAINT uq_giving_document_files_public_id UNIQUE (public_id),

    -- Só o que o navegador sabe exibir sem baixar. Aceitar qualquer tipo
    -- transformaria o campo num canal para subir executável.
    CONSTRAINT ck_giving_document_files_tipo
        CHECK (content_type IN ('application/pdf', 'image/png', 'image/jpeg')),

    -- 10 MB, o mesmo teto que a tela anuncia. Sem ele, um arquivo de 500 MB
    -- entraria e a linha inteira viraria um problema de memória a cada leitura.
    CONSTRAINT ck_giving_document_files_tamanho
        CHECK (size_bytes > 0 AND size_bytes <= 10485760),

    -- **O tamanho declarado tem de ser o tamanho real.**
    --
    -- Sem isto, um cliente que informasse `size_bytes = 1` passaria pelo teto
    -- acima e gravaria os 500 MB assim mesmo — o CHECK de tamanho estaria
    -- protegendo um número, não o arquivo.
    CONSTRAINT ck_giving_document_files_tamanho_real
        CHECK (octet_length(content) = size_bytes)
);

CREATE INDEX ix_giving_document_files_tenant ON giving_document_files (tenant_id);

ALTER TABLE giving_document_files ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_giving_document_files ON giving_document_files
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
