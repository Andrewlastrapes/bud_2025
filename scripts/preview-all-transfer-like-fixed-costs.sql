-- ============================================================================
-- Global Preview: Transfer-Like Fixed Costs
-- ============================================================================
-- Purpose: Preview all fixed costs across all users that match transfer-like
-- patterns. This is a READ-ONLY query for audit purposes.
--
-- NO DESTRUCTIVE CHANGES ARE MADE BY THIS SCRIPT.
--
-- Use this to identify transfer-like fixed costs that may have been incorrectly
-- created during onboarding before the TransferLikeClassifier was implemented.
--
-- After reviewing the results, individual user cleanup can be performed using
-- scoped scripts similar to cleanup-user-56-fixed-costs.sql.
-- ============================================================================

-- ── Transfer-like patterns to search for ───────────────────────────────────
-- Based on TransferLikeClassifier subtypes:
--   - CreditCardPayment: ONLINE PAYMENT, PAYMENT THANK YOU, CREDIT CARD PAYMENT, etc.
--   - WalletLoad: AMEX SEND, ADD MONEY
--   - OwnAccountTransfer: TRANSFER TO SAVINGS, ACCOUNT TRANSFER, etc.
--   - InvestmentTransfer: FIDELITY, VANGUARD, SCHWAB, ROBINHOOD, etc.

SELECT
    id,
    user_id,
    name,
    amount,
    category,
    type,
    plaid_merchant_name,
    recurrence_frequency,
    next_due_date,
    original_due_day_of_month,
    created_at,
    updated_at,
    CASE
        -- CreditCardPayment patterns
        WHEN name ILIKE '%ONLINE PAYMENT%THANK YOU%' THEN 'CreditCardPayment: ONLINE PAYMENT - THANK YOU'
        WHEN name ILIKE '%PAYMENT THANK YOU%' THEN 'CreditCardPayment: PAYMENT THANK YOU'
        WHEN name ILIKE '%CREDIT CARD PAYMENT%' THEN 'CreditCardPayment: CREDIT CARD PAYMENT'
        WHEN name ILIKE '%CREDIT CARD PMT%' THEN 'CreditCardPayment: CREDIT CARD PMT'
        WHEN name ILIKE '%CARD PAYMENT%' THEN 'CreditCardPayment: CARD PAYMENT'
        WHEN name ILIKE '%PAYMENT FROM CHK%' THEN 'CreditCardPayment: PAYMENT FROM CHK'
        WHEN name ILIKE '%CREDIT CARD AUTOPAY%' THEN 'CreditCardPayment: CREDIT CARD AUTOPAY'
        WHEN name ILIKE '%CARD AUTOPAY%' THEN 'CreditCardPayment: CARD AUTOPAY'
        
        -- WalletLoad patterns
        WHEN name ILIKE '%AMEX SEND%' THEN 'WalletLoad: AMEX SEND'
        
        -- OwnAccountTransfer patterns
        WHEN name ILIKE '%TRANSFER TO SAVINGS%' THEN 'OwnAccountTransfer: TRANSFER TO SAVINGS'
        WHEN name ILIKE '%TRANSFER TO CHECKING%' THEN 'OwnAccountTransfer: TRANSFER TO CHECKING'
        WHEN name ILIKE '%SAVINGS TRANSFER%' THEN 'OwnAccountTransfer: SAVINGS TRANSFER'
        WHEN name ILIKE '%ACCOUNT TRANSFER%' THEN 'OwnAccountTransfer: ACCOUNT TRANSFER'
        WHEN name ILIKE '%INTERNAL TRANSFER%' THEN 'OwnAccountTransfer: INTERNAL TRANSFER'
        WHEN name ILIKE '%OWN ACCOUNT%' THEN 'OwnAccountTransfer: OWN ACCOUNT'
        
        -- InvestmentTransfer patterns
        WHEN name ILIKE '%BROKERAGE TRANSFER%' THEN 'InvestmentTransfer: BROKERAGE TRANSFER'
        WHEN name ILIKE '%INVESTMENT TRANSFER%' THEN 'InvestmentTransfer: INVESTMENT TRANSFER'
        WHEN name ILIKE '%FIDELITY%' THEN 'InvestmentTransfer: FIDELITY'
        WHEN name ILIKE '%VANGUARD%' THEN 'InvestmentTransfer: VANGUARD'
        WHEN name ILIKE '%SCHWAB%' THEN 'InvestmentTransfer: SCHWAB'
        WHEN name ILIKE '%ROBINHOOD%' THEN 'InvestmentTransfer: ROBINHOOD'
        WHEN name ILIKE '%ETRADE%' OR name ILIKE '%E*TRADE%' OR name ILIKE '%E-TRADE%' THEN 'InvestmentTransfer: ETRADE'
        
        ELSE 'Unknown'
    END AS transfer_subtype
FROM "FixedCosts"
WHERE
    -- CreditCardPayment
    name ILIKE '%ONLINE PAYMENT%THANK YOU%'
    OR name ILIKE '%PAYMENT THANK YOU%'
    OR name ILIKE '%CREDIT CARD PAYMENT%'
    OR name ILIKE '%CREDIT CARD PMT%'
    OR name ILIKE '%CARD PAYMENT%'
    OR name ILIKE '%PAYMENT FROM CHK%'
    OR name ILIKE '%CREDIT CARD AUTOPAY%'
    OR name ILIKE '%CARD AUTOPAY%'
    
    -- WalletLoad
    OR name ILIKE '%AMEX SEND%'
    
    -- OwnAccountTransfer
    OR name ILIKE '%TRANSFER TO SAVINGS%'
    OR name ILIKE '%TRANSFER TO CHECKING%'
    OR name ILIKE '%SAVINGS TRANSFER%'
    OR name ILIKE '%ACCOUNT TRANSFER%'
    OR name ILIKE '%INTERNAL TRANSFER%'
    OR name ILIKE '%OWN ACCOUNT%'
    
    -- InvestmentTransfer
    OR name ILIKE '%BROKERAGE TRANSFER%'
    OR name ILIKE '%INVESTMENT TRANSFER%'
    OR name ILIKE '%FIDELITY%'
    OR name ILIKE '%VANGUARD%'
    OR name ILIKE '%SCHWAB%'
    OR name ILIKE '%ROBINHOOD%'
    OR name ILIKE '%ETRADE%'
    OR name ILIKE '%E*TRADE%'
    OR name ILIKE '%E-TRADE%'
ORDER BY user_id, transfer_subtype, name;

-- ── Summary by transfer subtype ────────────────────────────────────────────
SELECT
    CASE
        -- CreditCardPayment patterns
        WHEN name ILIKE '%ONLINE PAYMENT%THANK YOU%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%PAYMENT THANK YOU%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%CREDIT CARD PAYMENT%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%CREDIT CARD PMT%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%CARD PAYMENT%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%PAYMENT FROM CHK%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%CREDIT CARD AUTOPAY%' THEN 'CreditCardPayment'
        WHEN name ILIKE '%CARD AUTOPAY%' THEN 'CreditCardPayment'
        
        -- WalletLoad patterns
        WHEN name ILIKE '%AMEX SEND%' THEN 'WalletLoad'
        
        -- OwnAccountTransfer patterns
        WHEN name ILIKE '%TRANSFER TO SAVINGS%' THEN 'OwnAccountTransfer'
        WHEN name ILIKE '%TRANSFER TO CHECKING%' THEN 'OwnAccountTransfer'
        WHEN name ILIKE '%SAVINGS TRANSFER%' THEN 'OwnAccountTransfer'
        WHEN name ILIKE '%ACCOUNT TRANSFER%' THEN 'OwnAccountTransfer'
        WHEN name ILIKE '%INTERNAL TRANSFER%' THEN 'OwnAccountTransfer'
        WHEN name ILIKE '%OWN ACCOUNT%' THEN 'OwnAccountTransfer'
        
        -- InvestmentTransfer patterns
        WHEN name ILIKE '%BROKERAGE TRANSFER%' THEN 'InvestmentTransfer'
        WHEN name ILIKE '%INVESTMENT TRANSFER%' THEN 'InvestmentTransfer'
        WHEN name ILIKE '%FIDELITY%' THEN 'InvestmentTransfer'
        WHEN name ILIKE '%VANGUARD%' THEN 'InvestmentTransfer'
        WHEN name ILIKE '%SCHWAB%' THEN 'InvestmentTransfer'
        WHEN name ILIKE '%ROBINHOOD%' THEN 'InvestmentTransfer'
        WHEN name ILIKE '%ETRADE%' OR name ILIKE '%E*TRADE%' OR name ILIKE '%E-TRADE%' THEN 'InvestmentTransfer'
        
        ELSE 'Unknown'
    END AS transfer_subtype,
    COUNT(*) AS count,
    COUNT(DISTINCT user_id) AS affected_users,
    SUM(amount) AS total_amount
FROM "FixedCosts"
WHERE
    -- CreditCardPayment
    name ILIKE '%ONLINE PAYMENT%THANK YOU%'
    OR name ILIKE '%PAYMENT THANK YOU%'
    OR name ILIKE '%CREDIT CARD PAYMENT%'
    OR name ILIKE '%CREDIT CARD PMT%'
    OR name ILIKE '%CARD PAYMENT%'
    OR name ILIKE '%PAYMENT FROM CHK%'
    OR name ILIKE '%CREDIT CARD AUTOPAY%'
    OR name ILIKE '%CARD AUTOPAY%'
    
    -- WalletLoad
    OR name ILIKE '%AMEX SEND%'
    
    -- OwnAccountTransfer
    OR name ILIKE '%TRANSFER TO SAVINGS%'
    OR name ILIKE '%TRANSFER TO CHECKING%'
    OR name ILIKE '%SAVINGS TRANSFER%'
    OR name ILIKE '%ACCOUNT TRANSFER%'
    OR name ILIKE '%INTERNAL TRANSFER%'
    OR name ILIKE '%OWN ACCOUNT%'
    
    -- InvestmentTransfer
    OR name ILIKE '%BROKERAGE TRANSFER%'
    OR name ILIKE '%INVESTMENT TRANSFER%'
    OR name ILIKE '%FIDELITY%'
    OR name ILIKE '%VANGUARD%'
    OR name ILIKE '%SCHWAB%'
    OR name ILIKE '%ROBINHOOD%'
    OR name ILIKE '%ETRADE%'
    OR name ILIKE '%E*TRADE%'
    OR name ILIKE '%E-TRADE%'
GROUP BY transfer_subtype
ORDER BY count DESC;
