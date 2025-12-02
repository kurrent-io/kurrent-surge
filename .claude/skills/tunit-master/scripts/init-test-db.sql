-- PostgreSQL Test Database Initialization Script
-- Creates common test schema and seed data for integration tests

-- Enable necessary extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Create schemas
CREATE SCHEMA IF NOT EXISTS orders;
CREATE SCHEMA IF NOT EXISTS customers;
CREATE SCHEMA IF NOT EXISTS products;

-- ============================================================================
-- Customers Schema
-- ============================================================================

CREATE TABLE IF NOT EXISTS customers.customers (
    id VARCHAR(100) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    email VARCHAR(255) NOT NULL UNIQUE,
    phone VARCHAR(50),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_customers_email ON customers.customers(email);
CREATE INDEX idx_customers_created_at ON customers.customers(created_at);

-- ============================================================================
-- Products Schema
-- ============================================================================

CREATE TABLE IF NOT EXISTS products.products (
    id VARCHAR(100) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    price DECIMAL(10, 2) NOT NULL CHECK (price >= 0),
    stock_quantity INTEGER NOT NULL DEFAULT 0 CHECK (stock_quantity >= 0),
    category VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_products_category ON products.products(category);
CREATE INDEX idx_products_is_active ON products.products(is_active);
CREATE INDEX idx_products_price ON products.products(price);

-- ============================================================================
-- Orders Schema
-- ============================================================================

CREATE TABLE IF NOT EXISTS orders.orders (
    id SERIAL PRIMARY KEY,
    customer_id VARCHAR(100) NOT NULL,
    order_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    status VARCHAR(50) NOT NULL DEFAULT 'Pending',
    total_amount DECIMAL(10, 2) NOT NULL CHECK (total_amount >= 0),
    discount_amount DECIMAL(10, 2) DEFAULT 0 CHECK (discount_amount >= 0),
    shipping_cost DECIMAL(10, 2) DEFAULT 0 CHECK (shipping_cost >= 0),
    notes TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_customer FOREIGN KEY (customer_id)
        REFERENCES customers.customers(id)
        ON DELETE RESTRICT
);

CREATE INDEX idx_orders_customer_id ON orders.orders(customer_id);
CREATE INDEX idx_orders_order_date ON orders.orders(order_date);
CREATE INDEX idx_orders_status ON orders.orders(status);

CREATE TABLE IF NOT EXISTS orders.order_items (
    id SERIAL PRIMARY KEY,
    order_id INTEGER NOT NULL,
    product_id VARCHAR(100) NOT NULL,
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    unit_price DECIMAL(10, 2) NOT NULL CHECK (unit_price >= 0),
    subtotal DECIMAL(10, 2) NOT NULL CHECK (subtotal >= 0),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_order FOREIGN KEY (order_id)
        REFERENCES orders.orders(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_product FOREIGN KEY (product_id)
        REFERENCES products.products(id)
        ON DELETE RESTRICT
);

CREATE INDEX idx_order_items_order_id ON orders.order_items(order_id);
CREATE INDEX idx_order_items_product_id ON orders.order_items(product_id);

-- ============================================================================
-- Order Status History
-- ============================================================================

CREATE TABLE IF NOT EXISTS orders.order_status_history (
    id SERIAL PRIMARY KEY,
    order_id INTEGER NOT NULL,
    previous_status VARCHAR(50),
    new_status VARCHAR(50) NOT NULL,
    changed_by VARCHAR(100),
    notes TEXT,
    changed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_order_status_history FOREIGN KEY (order_id)
        REFERENCES orders.orders(id)
        ON DELETE CASCADE
);

CREATE INDEX idx_order_status_history_order_id ON orders.order_status_history(order_id);
CREATE INDEX idx_order_status_history_changed_at ON orders.order_status_history(changed_at);

-- ============================================================================
-- Functions and Triggers
-- ============================================================================

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Triggers for updated_at
CREATE TRIGGER update_customers_updated_at BEFORE UPDATE ON customers.customers
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_products_updated_at BEFORE UPDATE ON products.products
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_orders_updated_at BEFORE UPDATE ON orders.orders
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Function to calculate order total
CREATE OR REPLACE FUNCTION calculate_order_total(order_id_param INTEGER)
RETURNS DECIMAL AS $$
DECLARE
    items_total DECIMAL;
    order_discount DECIMAL;
    order_shipping DECIMAL;
    final_total DECIMAL;
BEGIN
    -- Calculate items total
    SELECT COALESCE(SUM(subtotal), 0) INTO items_total
    FROM orders.order_items
    WHERE order_id = order_id_param;

    -- Get discount and shipping
    SELECT discount_amount, shipping_cost INTO order_discount, order_shipping
    FROM orders.orders
    WHERE id = order_id_param;

    -- Calculate final total
    final_total := items_total - COALESCE(order_discount, 0) + COALESCE(order_shipping, 0);

    RETURN final_total;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Test Data Seed (Optional - comment out if not needed)
-- ============================================================================

-- Seed customers
INSERT INTO customers.customers (id, name, email, phone) VALUES
    ('customer-1', 'John Doe', 'john.doe@example.com', '+1-555-0101'),
    ('customer-2', 'Jane Smith', 'jane.smith@example.com', '+1-555-0102'),
    ('customer-3', 'Bob Johnson', 'bob.johnson@example.com', '+1-555-0103'),
    ('customer-4', 'Alice Williams', 'alice.williams@example.com', '+1-555-0104'),
    ('customer-5', 'Charlie Brown', 'charlie.brown@example.com', '+1-555-0105')
ON CONFLICT (id) DO NOTHING;

-- Seed products
INSERT INTO products.products (id, name, description, price, stock_quantity, category) VALUES
    ('prod-1', 'Laptop', 'High-performance laptop', 999.99, 50, 'Electronics'),
    ('prod-2', 'Mouse', 'Wireless mouse', 29.99, 200, 'Electronics'),
    ('prod-3', 'Keyboard', 'Mechanical keyboard', 89.99, 150, 'Electronics'),
    ('prod-4', 'Monitor', '27-inch 4K monitor', 449.99, 75, 'Electronics'),
    ('prod-5', 'Headphones', 'Noise-cancelling headphones', 199.99, 100, 'Electronics'),
    ('prod-6', 'Desk Chair', 'Ergonomic office chair', 299.99, 30, 'Furniture'),
    ('prod-7', 'Standing Desk', 'Adjustable standing desk', 599.99, 20, 'Furniture'),
    ('prod-8', 'Notebook', 'Professional notebook (pack of 5)', 19.99, 500, 'Office Supplies'),
    ('prod-9', 'Pen Set', 'Premium pen set', 39.99, 300, 'Office Supplies'),
    ('prod-10', 'USB-C Cable', '6ft USB-C cable', 14.99, 1000, 'Electronics')
ON CONFLICT (id) DO NOTHING;

-- Seed sample orders
INSERT INTO orders.orders (customer_id, order_date, status, total_amount, discount_amount, shipping_cost) VALUES
    ('customer-1', CURRENT_TIMESTAMP - INTERVAL '10 days', 'Delivered', 1119.97, 0, 15.00),
    ('customer-2', CURRENT_TIMESTAMP - INTERVAL '5 days', 'Shipped', 119.98, 10.00, 10.00),
    ('customer-3', CURRENT_TIMESTAMP - INTERVAL '2 days', 'Processing', 639.98, 0, 20.00),
    ('customer-4', CURRENT_TIMESTAMP - INTERVAL '1 day', 'Pending', 249.98, 0, 10.00),
    ('customer-5', CURRENT_TIMESTAMP, 'Pending', 44.98, 5.00, 5.00)
ON CONFLICT DO NOTHING;

-- Seed order items (for order 1)
INSERT INTO orders.order_items (order_id, product_id, quantity, unit_price, subtotal) VALUES
    (1, 'prod-1', 1, 999.99, 999.99),
    (1, 'prod-2', 2, 29.99, 59.98),
    (1, 'prod-3', 1, 89.99, 89.99);

-- Seed order items (for order 2)
INSERT INTO orders.order_items (order_id, product_id, quantity, unit_price, subtotal) VALUES
    (2, 'prod-2', 2, 29.99, 59.98),
    (2, 'prod-8', 3, 19.99, 59.97);

-- Seed order items (for order 3)
INSERT INTO orders.order_items (order_id, product_id, quantity, unit_price, subtotal) VALUES
    (3, 'prod-4', 1, 449.99, 449.99),
    (3, 'prod-5', 1, 199.99, 199.99);

-- Seed order items (for order 4)
INSERT INTO orders.order_items (order_id, product_id, quantity, unit_price, subtotal) VALUES
    (4, 'prod-6', 1, 299.99, 299.99);

-- Seed order items (for order 5)
INSERT INTO orders.order_items (order_id, product_id, quantity, unit_price, subtotal) VALUES
    (5, 'prod-9', 1, 39.99, 39.99),
    (5, 'prod-10', 1, 14.99, 14.99);

-- ============================================================================
-- Views for Testing
-- ============================================================================

CREATE OR REPLACE VIEW orders.order_summary AS
SELECT
    o.id,
    o.customer_id,
    c.name AS customer_name,
    c.email AS customer_email,
    o.order_date,
    o.status,
    COUNT(oi.id) AS item_count,
    SUM(oi.quantity) AS total_quantity,
    o.total_amount,
    o.discount_amount,
    o.shipping_cost
FROM orders.orders o
JOIN customers.customers c ON o.customer_id = c.id
LEFT JOIN orders.order_items oi ON o.id = oi.order_id
GROUP BY o.id, o.customer_id, c.name, c.email, o.order_date, o.status,
         o.total_amount, o.discount_amount, o.shipping_cost;

-- ============================================================================
-- Grant Permissions
-- ============================================================================

GRANT ALL PRIVILEGES ON SCHEMA customers TO testuser;
GRANT ALL PRIVILEGES ON SCHEMA products TO testuser;
GRANT ALL PRIVILEGES ON SCHEMA orders TO testuser;

GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA customers TO testuser;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA products TO testuser;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA orders TO testuser;

GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA orders TO testuser;

-- ============================================================================
-- Complete
-- ============================================================================

SELECT 'Test database initialized successfully!' AS status;
