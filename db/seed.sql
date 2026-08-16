-- =============================================================================
-- Congrega — seed de papéis e permissões
-- =============================================================================
-- Sem estas linhas, toda policy da API reprova: `PermissionRequirement` consulta
-- a claim `perms`, que é montada a partir de role_permissions. Um banco sem seed
-- autentica o usuário e nega tudo em seguida — falha confusa, porque o login
-- funciona e nenhuma tela abre.
--
-- Idempotente de propósito: roda em toda inicialização de ambiente e em toda
-- migração, sem duplicar nem falhar.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Permissões
-- -----------------------------------------------------------------------------
-- Os códigos precisam bater EXATAMENTE com a classe `Permissions` em
-- Congrega.Domain.Tenancy. Divergir aqui produz o pior tipo de bug de
-- autorização: silencioso, e que só aparece quando alguém tenta usar a tela.
INSERT INTO permissions (code, name) VALUES
    ('members.read',       'Ver membros'),
    ('members.write',      'Cadastrar e editar membros'),
    ('giving.read',        'Ver contribuições'),
    ('giving.write',       'Lançar contribuições'),
    ('children.read',      'Ver fichas de crianças'),
    ('children.checkin',   'Registrar entrada de criança'),
    ('children.checkout',  'Autorizar retirada de criança'),
    ('events.write',       'Criar e editar eventos'),
    ('billing.manage',     'Gerenciar assinatura da igreja')
ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name;

-- -----------------------------------------------------------------------------
-- Papéis de sistema
-- -----------------------------------------------------------------------------
-- tenant_id NULL = disponível para todos os tenants. O UNIQUE NULLS NOT DISTINCT
-- da tabela é o que impede duplicar estas linhas a cada execução.
INSERT INTO roles (code, name, is_system, tenant_id) VALUES
    ('ChurchAdmin',    'Administrador da igreja', TRUE, NULL),
    ('Treasurer',      'Tesoureiro',              TRUE, NULL),
    ('CellLeader',     'Líder de célula',         TRUE, NULL),
    ('ChildcareStaff', 'Ministério infantil',     TRUE, NULL),
    ('Member',         'Membro',                  TRUE, NULL)
ON CONFLICT (tenant_id, code) DO UPDATE SET name = EXCLUDED.name;

-- -----------------------------------------------------------------------------
-- Papel → permissões
-- -----------------------------------------------------------------------------
-- Escrito como lista de pares para que a concessão seja legível na revisão de
-- código. Uma matriz seria mais curta e muito mais fácil de errar em silêncio.
WITH concessoes (role_code, permission_code) AS (
    VALUES
        -- Administrador: tudo, exceto o que exige preparo específico.
        ('ChurchAdmin', 'members.read'),
        ('ChurchAdmin', 'members.write'),
        ('ChurchAdmin', 'giving.read'),
        ('ChurchAdmin', 'events.write'),
        ('ChurchAdmin', 'children.read'),
        ('ChurchAdmin', 'billing.manage'),

        -- Tesoureiro: lança dinheiro, e SÓ dinheiro. Não vê ficha de criança.
        -- Menor privilégio aplicado ao caso concreto: não há razão operacional
        -- para a tesouraria acessar alergia e foto de menor de idade.
        ('Treasurer', 'giving.read'),
        ('Treasurer', 'giving.write'),
        ('Treasurer', 'members.read'),

        -- Líder de célula: enxerga membros para acompanhar o grupo, e nada de
        -- financeiro — evita o constrangimento de o líder saber quanto cada
        -- pessoa da célula contribuiu.
        ('CellLeader', 'members.read'),
        ('CellLeader', 'events.write'),

        -- Ministério infantil: as permissões mais sensíveis do sistema, dadas
        -- exclusivamente a quem opera o berçário no dia do culto.
        ('ChildcareStaff', 'children.read'),
        ('ChildcareStaff', 'children.checkin'),
        ('ChildcareStaff', 'children.checkout'),
        ('ChildcareStaff', 'members.read'),

        -- Membro comum: nenhuma permissão administrativa. O acesso ao próprio
        -- perfil e ao conteúdo Congrega+ não passa por papel — o primeiro é
        -- posse do recurso, o segundo é entitlement.
        ('Member', 'members.read')
)
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM concessoes c
  JOIN roles r       ON r.code = c.role_code AND r.tenant_id IS NULL
  JOIN permissions p ON p.code = c.permission_code
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- -----------------------------------------------------------------------------
-- Verificação
-- -----------------------------------------------------------------------------
DO $$
DECLARE
    total_papeis      INT;
    total_permissoes  INT;
    total_concessoes  INT;
BEGIN
    SELECT COUNT(*) INTO total_papeis      FROM roles WHERE is_system;
    SELECT COUNT(*) INTO total_permissoes  FROM permissions;
    SELECT COUNT(*) INTO total_concessoes  FROM role_permissions;

    RAISE NOTICE 'Seed: % papéis, % permissões, % concessões.',
        total_papeis, total_permissoes, total_concessoes;

    -- Falha alto se o seed não completou. Um banco pela metade produziria
    -- negações de autorização que ninguém liga à causa.
    IF total_papeis < 5 OR total_permissoes < 9 THEN
        RAISE EXCEPTION 'Seed incompleto: % papéis e % permissões.',
            total_papeis, total_permissoes;
    END IF;
END $$;
