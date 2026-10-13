-- ============================================================================
-- TUTUMLU - ANA VERİTABANI ŞEMASI (schema.sql)
-- ============================================================================
-- Bu dosya projenin tüm güncel tablolarını, yabancı anahtar (FK) ilişkilerini,
-- kısıtlamalarını ve performans indekslerini tek bir yerde toplar.
-- 
-- Özellikler:
-- 1. Idempotent (IF NOT EXISTS): Sıfır veritabanında her şeyi oluşturur,
--    dolu veritabanında var olan verilere dokunmaz, eksikleri ekler.
-- 2. SSMS / Azure Data Studio / sqlcmd ile F5 (Execute) yapılarak doğrudan çalıştırılabilir.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ----------------------------------------------------------------------------
-- 1. BRANDS (Markalar)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Brands')
BEGIN
    CREATE TABLE dbo.Brands (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(255) NOT NULL UNIQUE,
        LogoUrl NVARCHAR(MAX) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- ----------------------------------------------------------------------------
-- 2. CATEGORIES (Kategoriler - Self Referencing & Font Awesome v4.7 Destekli)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Categories')
BEGIN
    CREATE TABLE dbo.Categories (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(255) NOT NULL,
        ParentCategoryId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id),
        SectorId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id),
        Slug NVARCHAR(255) NOT NULL,
        CategoryPath NVARCHAR(1000) NOT NULL DEFAULT '',
        Depth INT NOT NULL DEFAULT 0,
        IconClass NVARCHAR(100) NOT NULL DEFAULT 'fa fa-cube',
        DisplayOrder INT NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Categories') AND name = 'IconClass')
        ALTER TABLE dbo.Categories ADD IconClass NVARCHAR(100) NOT NULL CONSTRAINT DF_Categories_IconClass DEFAULT 'fa fa-cube';
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Categories') AND name = 'CategoryPath')
        ALTER TABLE dbo.Categories ADD CategoryPath NVARCHAR(1000) NOT NULL CONSTRAINT DF_Categories_CategoryPath DEFAULT '';
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Categories') AND name = 'Depth')
        ALTER TABLE dbo.Categories ADD Depth INT NOT NULL CONSTRAINT DF_Categories_Depth DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Categories') AND name = 'DisplayOrder')
        ALTER TABLE dbo.Categories ADD DisplayOrder INT NOT NULL CONSTRAINT DF_Categories_DisplayOrder DEFAULT 0;
END
GO

-- Slug tekilliği sektör bazlıdır (Market vs Mobilya aynı ada sahip alt kategoriye izin verir)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UQ_Categories_Slug_SectorId')
BEGIN
    CREATE UNIQUE INDEX UQ_Categories_Slug_SectorId ON dbo.Categories (Slug, SectorId);
END
GO

-- ----------------------------------------------------------------------------
-- 2B. CATEGORYATTRIBUTES (Kategori Bazlı Dinamik Filtre & Öznitelik Tanımları)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CategoryAttributes')
BEGIN
    CREATE TABLE dbo.CategoryAttributes (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SectorId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id),
        CategoryId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id),
        AttributeName NVARCHAR(100) NOT NULL,
        DisplayName NVARCHAR(100) NOT NULL,
        DisplayOrder INT NOT NULL DEFAULT 0,
        IsFilterable BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_CategoryAttributes_Scope_Name UNIQUE (SectorId, CategoryId, AttributeName)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CategoryAttributes_Lookup')
BEGIN
    CREATE INDEX IX_CategoryAttributes_Lookup ON dbo.CategoryAttributes (SectorId, CategoryId, DisplayOrder);
END
GO

-- ----------------------------------------------------------------------------
-- 3. SELLERS (Satıcılar / Mağazalar)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sellers')
BEGIN
    CREATE TABLE dbo.Sellers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Name NVARCHAR(255) NOT NULL UNIQUE,
        WebSiteUrl NVARCHAR(MAX) NULL,
        IconClass NVARCHAR(100) NOT NULL DEFAULT 'fa fa-building',
        Rating DECIMAL(3,2) NOT NULL DEFAULT 0.00,
        IsActive BIT NOT NULL DEFAULT 1
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Sellers') AND name = 'IconClass')
        ALTER TABLE dbo.Sellers ADD IconClass NVARCHAR(100) NOT NULL CONSTRAINT DF_Sellers_IconClass DEFAULT 'fa fa-building';
END
GO

-- ----------------------------------------------------------------------------
-- 4. PRODUCTS (Kanonik / Eşleştirilmiş Ana Ürünler)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Products')
BEGIN
    CREATE TABLE dbo.Products (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        BrandId INT NOT NULL FOREIGN KEY REFERENCES dbo.Brands(Id),
        CategoryId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id),
        Name NVARCHAR(500) NOT NULL,
        Sku NVARCHAR(100) NULL,
        ImageUrl NVARCHAR(MAX) NULL,
        SizeValue DECIMAL(18,3) NULL,
        SizeUnit NVARCHAR(50) NULL,
        PackQuantity INT NOT NULL DEFAULT 1,
        SpecsFetchedAt DATETIME NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Products') AND name = 'SpecsFetchedAt')
        ALTER TABLE dbo.Products ADD SpecsFetchedAt DATETIME NULL;
END
GO

-- ----------------------------------------------------------------------------
-- 5. SELLERPRODUCTS (Kazınan Ham Mağaza Ürünleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SellerProducts')
BEGIN
    CREATE TABLE dbo.SellerProducts (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SellerId INT NOT NULL FOREIGN KEY REFERENCES dbo.Sellers(Id),
        SellerProductCode NVARCHAR(255) NOT NULL,
        ExternalTitle NVARCHAR(500) NOT NULL,
        ExternalUrl NVARCHAR(MAX) NULL,
        ExternalImageUrl NVARCHAR(MAX) NULL,
        CurrentPrice DECIMAL(18,2) NOT NULL,
        RegularPrice DECIMAL(18,2) NULL,
        UnitType NVARCHAR(20) NULL,
        UnitAmount DECIMAL(18,3) NULL,
        SourceUnitPrice DECIMAL(18,2) NULL,
        StoreName NVARCHAR(100) NULL,
        OriginalCategory NVARCHAR(500) NULL,
        LastScrapedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_SellerProducts_Seller_Code UNIQUE (SellerId, SellerProductCode)
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SellerProducts') AND name = 'RegularPrice')
        ALTER TABLE dbo.SellerProducts ADD RegularPrice DECIMAL(18,2) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SellerProducts') AND name = 'StoreName')
        ALTER TABLE dbo.SellerProducts ADD StoreName NVARCHAR(100) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SellerProducts') AND name = 'OriginalCategory')
        ALTER TABLE dbo.SellerProducts ADD OriginalCategory NVARCHAR(500) NULL;
END
GO

-- ----------------------------------------------------------------------------
-- 5B. SELLERCATEGORIES (Mağaza Kategori Haritalama ve Reyon Linkleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SellerCategories')
BEGIN
    CREATE TABLE dbo.SellerCategories (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SellerId INT NOT NULL FOREIGN KEY REFERENCES dbo.Sellers(Id),
        CategoryName NVARCHAR(255) NOT NULL,
        CategoryPath NVARCHAR(500) NULL,
        ExternalCode NVARCHAR(100) NULL,
        Url NVARCHAR(MAX) NOT NULL,
        TargetCategoryId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id) ON DELETE SET NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        ProductCount INT NOT NULL DEFAULT 0,
        LastScrapedAt DATETIME NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_SellerCategories_Seller_Code UNIQUE (SellerId, ExternalCode)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SellerCategories_TargetCategoryId')
BEGIN
    CREATE INDEX IX_SellerCategories_TargetCategoryId ON dbo.SellerCategories (TargetCategoryId);
END
GO

-- ----------------------------------------------------------------------------
-- 6. OFFERS (Güncel Satıcı Teklifleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Offers')
BEGIN
    CREATE TABLE dbo.Offers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id),
        SellerId INT NOT NULL FOREIGN KEY REFERENCES dbo.Sellers(Id),
        SellerProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.SellerProducts(Id),
        Price DECIMAL(18,2) NOT NULL,
        RegularPrice DECIMAL(18,2) NULL,
        CargoPrice DECIMAL(18,2) NOT NULL DEFAULT 0.00,
        IsActive BIT NOT NULL DEFAULT 1,
        LastUpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        TotalCost AS (Price + CargoPrice) PERSISTED,
        CONSTRAINT UQ_Offers_Product_Seller UNIQUE (ProductId, SellerId)
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Offers') AND name = 'RegularPrice')
        ALTER TABLE dbo.Offers ADD RegularPrice DECIMAL(18,2) NULL;
END
GO

-- ----------------------------------------------------------------------------
-- 7. OFFERSPRICEHISTORY (Teklif Fiyat Değişim Geçmişi)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OffersPriceHistory')
BEGIN
    CREATE TABLE dbo.OffersPriceHistory (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        OfferId INT NOT NULL FOREIGN KEY REFERENCES dbo.Offers(Id) ON DELETE CASCADE,
        Price DECIMAL(18,2) NOT NULL,
        RecordedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- ----------------------------------------------------------------------------
-- 8. PRODUCTATTRIBUTES (Filtreleme Etiketleri / Özellikleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductAttributes')
BEGIN
    CREATE TABLE dbo.ProductAttributes (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE CASCADE,
        AttributeName NVARCHAR(50) NOT NULL,
        AttributeValue NVARCHAR(100) NOT NULL,
        CONSTRAINT UQ_ProductAttributes UNIQUE (ProductId, AttributeName, AttributeValue)
    );
END
GO

-- ----------------------------------------------------------------------------
-- 9. PRODUCTSPECS (Detay Sayfası Besin/Teknik Özellikleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductSpecs')
BEGIN
    CREATE TABLE dbo.ProductSpecs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE CASCADE,
        SpecGroup NVARCHAR(200) NOT NULL,
        SpecName NVARCHAR(200) NOT NULL,
        SpecValue NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_ProductSpecs UNIQUE (ProductId, SpecGroup, SpecName)
    );
END
GO

-- ----------------------------------------------------------------------------
-- 10. PRODUCTMATCHINGLOGS (Eşleştirme Denetim Logları)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductMatchingLogs')
BEGIN
    CREATE TABLE dbo.ProductMatchingLogs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SellerProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.SellerProducts(Id),
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id),
        Status NVARCHAR(50) NOT NULL,
        MatchedBy NVARCHAR(100) NOT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        Notes NVARCHAR(MAX) NULL
    );
END
GO

-- ----------------------------------------------------------------------------
-- 11. DAILYSEARCHTRENDS (Günlük Popüler Arama Terimleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DailySearchTrends')
BEGIN
    CREATE TABLE dbo.DailySearchTrends (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        SearchDate DATE NOT NULL,
        SearchTerm NVARCHAR(100) NOT NULL,
        SearchCount INT NOT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_DailySearchTrends_Date_Term UNIQUE (SearchDate, SearchTerm)
    );
END
GO

-- ----------------------------------------------------------------------------
-- 12. USERS (Kullanıcılar ve Genişletilmiş Profil)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE dbo.Users (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Email NVARCHAR(255) NOT NULL UNIQUE,
        PasswordHash NVARCHAR(255) NOT NULL,
        DisplayName NVARCHAR(255) NULL,
        Name NVARCHAR(255) NULL,
        Surname NVARCHAR(255) NULL,
        Phone NVARCHAR(20) NULL,
        BirthDate DATE NULL,
        City NVARCHAR(100) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Name')
        ALTER TABLE dbo.Users ADD Name NVARCHAR(255) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Surname')
        ALTER TABLE dbo.Users ADD Surname NVARCHAR(255) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Phone')
        ALTER TABLE dbo.Users ADD Phone NVARCHAR(20) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'BirthDate')
        ALTER TABLE dbo.Users ADD BirthDate DATE NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'City')
        ALTER TABLE dbo.Users ADD City NVARCHAR(100) NULL;
END
GO

-- ----------------------------------------------------------------------------
-- 13. FAVORITES (Favori Ürünler)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Favorites')
BEGIN
    CREATE TABLE dbo.Favorites (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE CASCADE,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_Favorites_User_Product UNIQUE (UserId, ProductId)
    );
END
GO

-- ----------------------------------------------------------------------------
-- 14. NOTIFICATIONS (İndirim ve Kampanya Bildirimleri)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Notifications')
BEGIN
    CREATE TABLE dbo.Notifications (
        NotificationId INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        OnSaleProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE CASCADE,
        DiscountedPrice DECIMAL(18,2) NOT NULL,
        NotificationMessage NVARCHAR(500) NOT NULL,
        NotificationDate DATETIME NOT NULL DEFAULT GETUTCDATE(),
        ReadStatus BIT NOT NULL DEFAULT 0
    );
END
GO

-- ----------------------------------------------------------------------------
-- 15. USERSEARCHHISTORY (Kullanıcı Arama Geçmişi)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserSearchHistory')
BEGIN
    CREATE TABLE dbo.UserSearchHistory (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        SearchTerm NVARCHAR(200) NOT NULL,
        ProductId INT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE SET NULL,
        CategoryId INT NULL FOREIGN KEY REFERENCES dbo.Categories(Id) ON DELETE SET NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- ----------------------------------------------------------------------------
-- 16. USERPRODUCTVIEWS (Kullanıcı Ürün İnceleme Geçmişi)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserProductViews')
BEGIN
    CREATE TABLE dbo.UserProductViews (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE CASCADE,
        ViewedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- ----------------------------------------------------------------------------
-- 17. PURCHASEHISTORY (Kullanıcı Satın Alma Geçmişi)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PurchaseHistory')
BEGIN
    CREATE TABLE dbo.PurchaseHistory (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL FOREIGN KEY REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        ProductId INT NOT NULL FOREIGN KEY REFERENCES dbo.Products(Id) ON DELETE CASCADE,
        SellerId INT NOT NULL FOREIGN KEY REFERENCES dbo.Sellers(Id),
        ActualPrice DECIMAL(18,2) NOT NULL DEFAULT 0,
        DiscountedPrice DECIMAL(18,2) NOT NULL DEFAULT 0,
        PurchaseDate DATETIME NOT NULL DEFAULT GETUTCDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PurchaseHistory') AND name = 'ActualPrice')
        ALTER TABLE dbo.PurchaseHistory ADD ActualPrice DECIMAL(18,2) NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PurchaseHistory') AND name = 'DiscountedPrice')
        ALTER TABLE dbo.PurchaseHistory ADD DiscountedPrice DECIMAL(18,2) NOT NULL DEFAULT 0;
END
GO

-- ============================================================================
-- PERFORMANS İNDEKSLERİ
-- ============================================================================

-- Offers İndeksleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Offers_ProductId_Price')
    CREATE INDEX IX_Offers_ProductId_Price ON dbo.Offers(ProductId, Price) INCLUDE (RegularPrice);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Offers_SellerId')
    CREATE INDEX IX_Offers_SellerId ON dbo.Offers(SellerId) INCLUDE (ProductId, Price, SellerProductId, RegularPrice);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Offers_SellerProductId')
    CREATE INDEX IX_Offers_SellerProductId ON dbo.Offers(SellerProductId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Offers_ProductId_Covering')
    CREATE INDEX IX_Offers_ProductId_Covering ON dbo.Offers(ProductId) INCLUDE (Id, SellerId, SellerProductId, Price, RegularPrice);
GO

-- Products İndeksleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_BrandId')
    CREATE INDEX IX_Products_BrandId ON dbo.Products(BrandId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_CategoryId')
    CREATE INDEX IX_Products_CategoryId ON dbo.Products(CategoryId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_Name')
    CREATE INDEX IX_Products_Name ON dbo.Products(Name) INCLUDE (BrandId, CategoryId, ImageUrl, SizeValue, SizeUnit, PackQuantity, CreatedAt);
GO

-- Categories İndeksleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_ParentCategoryId')
    CREATE INDEX IX_Categories_ParentCategoryId ON dbo.Categories(ParentCategoryId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_SectorId')
    CREATE INDEX IX_Categories_SectorId ON dbo.Categories(SectorId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_Slug')
    CREATE INDEX IX_Categories_Slug ON dbo.Categories(Slug);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_Name')
    CREATE INDEX IX_Categories_Name ON dbo.Categories(Name);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_Depth')
    CREATE INDEX IX_Categories_Depth ON dbo.Categories(Depth, DisplayOrder);
GO

-- ProductAttributes & Specs İndeksleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductAttributes_ProductId')
    CREATE INDEX IX_ProductAttributes_ProductId ON dbo.ProductAttributes(ProductId, AttributeName, AttributeValue);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductSpecs_ProductId')
    CREATE INDEX IX_ProductSpecs_ProductId ON dbo.ProductSpecs(ProductId);
GO

-- Kullanıcı ve Etkileşim İndeksleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Favorites_UserId')
    CREATE INDEX IX_Favorites_UserId ON dbo.Favorites(UserId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Notifications_UserId_ReadStatus')
    CREATE INDEX IX_Notifications_UserId_ReadStatus ON dbo.Notifications(UserId, ReadStatus);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Notifications_UserId_Product_Date')
    CREATE INDEX IX_Notifications_UserId_Product_Date ON dbo.Notifications(UserId, OnSaleProductId, NotificationDate DESC);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserSearchHistory_UserId')
    CREATE INDEX IX_UserSearchHistory_UserId ON dbo.UserSearchHistory(UserId, CreatedAt DESC);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_UserProductViews_UserId')
    CREATE INDEX IX_UserProductViews_UserId ON dbo.UserProductViews(UserId, ViewedAt DESC);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PurchaseHistory_UserId')
    CREATE INDEX IX_PurchaseHistory_UserId ON dbo.PurchaseHistory(UserId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PurchaseHistory_ProductId')
    CREATE INDEX IX_PurchaseHistory_ProductId ON dbo.PurchaseHistory(ProductId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PurchaseHistory_SellerId')
    CREATE INDEX IX_PurchaseHistory_SellerId ON dbo.PurchaseHistory(SellerId);
GO

-- ----------------------------------------------------------------------------
-- 18. SHOPCATEGORIES (Ham Mağaza/Vendor Kategori Ağaçları)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ShopCategories')
BEGIN
    CREATE TABLE dbo.ShopCategories (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        ShopId INT NOT NULL FOREIGN KEY REFERENCES dbo.Sellers(Id) ON DELETE CASCADE,
        VendorExternalId NVARCHAR(100) NOT NULL,
        ParentVendorId NVARCHAR(100) NULL,
        ParentId BIGINT NULL FOREIGN KEY REFERENCES dbo.ShopCategories(Id),
        Name NVARCHAR(255) NOT NULL,
        RawPath NVARCHAR(MAX) NOT NULL,
        NormalizedPath NVARCHAR(MAX) NOT NULL,
        Depth INT NOT NULL DEFAULT 0,
        LastSeenAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_ShopCategories_Shop_VendorId UNIQUE (ShopId, VendorExternalId)
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ShopCategories_Shop_Parent')
    CREATE INDEX IX_ShopCategories_Shop_Parent ON dbo.ShopCategories(ShopId, ParentId);
GO

-- ----------------------------------------------------------------------------
-- 19. CATEGORYMAPPINGS (Dinamik Mağaza -> Kanonik Kategori İlişkisi)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CategoryMappings')
BEGIN
    CREATE TABLE dbo.CategoryMappings (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        ShopCategoryId BIGINT NOT NULL UNIQUE FOREIGN KEY REFERENCES dbo.ShopCategories(Id) ON DELETE CASCADE,
        CanonicalCategoryId INT NOT NULL FOREIGN KEY REFERENCES dbo.Categories(Id),
        ConfidenceScore DECIMAL(5,4) NOT NULL DEFAULT 0.0000,
        MappedBy NVARCHAR(50) NOT NULL DEFAULT 'HEURISTIC',
        IsVerified BIT NOT NULL DEFAULT 0,
        VerifiedByUserId INT NULL,
        Notes NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CategoryMappings_Canonical')
    CREATE INDEX IX_CategoryMappings_Canonical ON dbo.CategoryMappings(CanonicalCategoryId);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CategoryMappings_Verification')
    CREATE INDEX IX_CategoryMappings_Verification ON dbo.CategoryMappings(IsVerified, ConfidenceScore);
GO

PRINT 'Tutumlu veritabanı şeması ve indeksleri başarıyla doğrulandı/oluşturuldu.';
GO

