-- ============================================================================
-- TUTUMLU - KATEGORİ SEED VERİSİ (seed_categories.sql)
-- Kaynak: mutlak-kategori.pdf
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Mevcut kategorileri ve kategoriye bağlı foreign key referanslarını temizle
UPDATE dbo.Products SET CategoryId = NULL;
UPDATE dbo.UserSearchHistory SET CategoryId = NULL;
DELETE FROM dbo.Categories;
DBCC CHECKIDENT ('dbo.Categories', RESEED, 0);
GO

-- ============================================================================
-- SEVİYE 0: ANA SEKTÖR KÖKLERİ (6 Adet - Font Awesome v4.7 İkonlarıyla)
-- ============================================================================
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IconClass, IsActive)
VALUES (N'Ofis Kırtasiye & Kağıt Ürünleri', NULL, N'ofis-kirtasiye-ve-kagit-urunleri', NULL, N'fa fa-pencil-square-o', 1);
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IconClass, IsActive)
VALUES (N'Temizlik ve Sağlık', NULL, N'temizlik-ve-saglik', NULL, N'fa fa-medkit', 1);
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IconClass, IsActive)
VALUES (N'Gıda ve Mutfak', NULL, N'gida-ve-mutfak', NULL, N'fa fa-cutlery', 1);
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IconClass, IsActive)
VALUES (N'Kartuş - Toner', NULL, N'kartus-toner', NULL, N'fa fa-print', 1);
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IconClass, IsActive)
VALUES (N'Mobilya ve Hırdavat', NULL, N'mobilya-ve-hirdavat', NULL, N'fa fa-wrench', 1);
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IconClass, IsActive)
VALUES (N'Teknoloji', NULL, N'teknoloji', NULL, N'fa fa-laptop', 1);
GO

-- Kök sektörlerin SectorId alanını kendi Id'lerine eşitle
UPDATE dbo.Categories SET SectorId = Id WHERE ParentCategoryId IS NULL;
GO

-- ============================================================================
-- SEKTÖR 1: OFIS KıRTASIYE & KAĞıT ÜRÜNLERI
-- ============================================================================
DECLARE @RootId_0 INT = (SELECT Id FROM dbo.Categories WHERE Slug = N'ofis-kirtasiye-ve-kagit-urunleri' AND ParentCategoryId IS NULL);

-- Seviye 1: Kalemler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kalemler', @RootId_0, N'kalemler', @RootId_0, 1);
DECLARE @SubId_0_0 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kalemler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Asetat Kalemleri', @SubId_0_0, N'asetat-kalemleri', @RootId_0, 1),
(N'Cam ve Boya Marker Kalem', @SubId_0_0, N'cam-ve-boya-marker-kalem', @RootId_0, 1),
(N'Dolma Kalemler', @SubId_0_0, N'dolma-kalemler', @RootId_0, 1),
(N'Fineliner Kalem', @SubId_0_0, N'fineliner-kalem', @RootId_0, 1),
(N'Fosforlu Kalemler', @SubId_0_0, N'fosforlu-kalemler', @RootId_0, 1),
(N'İğne Uç Kalemler', @SubId_0_0, N'igne-uc-kalemler', @RootId_0, 1),
(N'Jel Kalem', @SubId_0_0, N'jel-kalem', @RootId_0, 1),
(N'İmza Kalemleri', @SubId_0_0, N'imza-kalemleri', @RootId_0, 1),
(N'Kalem Uçları', @SubId_0_0, N'kalem-uclari', @RootId_0, 1),
(N'Keçeli Kalemler', @SubId_0_0, N'keceli-kalemler', @RootId_0, 1),
(N'Kurşun Kalemler', @SubId_0_0, N'kursun-kalemler', @RootId_0, 1),
(N'Kalem Mürekkebi', @SubId_0_0, N'kalem-murekkebi', @RootId_0, 1),
(N'Kaligrafi Kalemleri', @SubId_0_0, N'kaligrafi-kalemleri', @RootId_0, 1),
(N'Marker Kalem', @SubId_0_0, N'marker-kalem', @RootId_0, 1),
(N'Prestij Kalemler', @SubId_0_0, N'prestij-kalemler', @RootId_0, 1),
(N'Roller Kalemler', @SubId_0_0, N'roller-kalemler', @RootId_0, 1),
(N'Tükenmez Kalemler', @SubId_0_0, N'tukenmez-kalemler', @RootId_0, 1),
(N'Tahta Kalemi', @SubId_0_0, N'tahta-kalemi', @RootId_0, 1),
(N'Teknik Çizim Kalemleri', @SubId_0_0, N'teknik-cizim-kalemleri', @RootId_0, 1),
(N'Versatil Uçlu Kalemler', @SubId_0_0, N'versatil-uclu-kalemler', @RootId_0, 1);

-- Seviye 1: Defterler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Defterler', @RootId_0, N'defterler', @RootId_0, 1);
DECLARE @SubId_0_1 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Defterler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Ofis Defterleri', @SubId_0_1, N'ofis-defterleri', @RootId_0, 1),
(N'Okul Defterleri', @SubId_0_1, N'okul-defterleri', @RootId_0, 1),
(N'Bloknotlar', @SubId_0_1, N'bloknotlar', @RootId_0, 1),
(N'Ticari Defter ve Evraklar', @SubId_0_1, N'ticari-defter-ve-evraklar', @RootId_0, 1),
(N'Fihristler', @SubId_0_1, N'fihristler', @RootId_0, 1);

-- Seviye 1: Masaüstü Gereçleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Masaüstü Gereçleri', @RootId_0, N'masaustu-gerecleri', @RootId_0, 1);
DECLARE @SubId_0_2 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Masaüstü Gereçleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Ataşlar', @SubId_0_2, N'ataslar', @RootId_0, 1),
(N'Bant', @SubId_0_2, N'bant', @RootId_0, 1),
(N'Bant Kesici', @SubId_0_2, N'bant-kesici', @RootId_0, 1),
(N'Dekoratif Masaüstü Ürünler', @SubId_0_2, N'dekoratif-masaustu-urunler', @RootId_0, 1),
(N'Daksil', @SubId_0_2, N'daksil', @RootId_0, 1),
(N'Delgeçler', @SubId_0_2, N'delgecler', @RootId_0, 1),
(N'Evrak Mandalları', @SubId_0_2, N'evrak-mandallari', @RootId_0, 1),
(N'Evrak Rafları', @SubId_0_2, N'evrak-raflari', @RootId_0, 1),
(N'Harita Çivisi', @SubId_0_2, N'harita-civisi', @RootId_0, 1),
(N'Raptiye', @SubId_0_2, N'raptiye', @RootId_0, 1),
(N'İğneler', @SubId_0_2, N'igneler', @RootId_0, 1),
(N'Hesap Makineleri', @SubId_0_2, N'hesap-makineleri', @RootId_0, 1),
(N'Kalemtıraşlar', @SubId_0_2, N'kalemtiraslar', @RootId_0, 1),
(N'Kartvizitlikler', @SubId_0_2, N'kartvizitlikler', @RootId_0, 1),
(N'Kaşe ve Gereçleri', @SubId_0_2, N'kase-ve-gerecleri', @RootId_0, 1),
(N'Magazinlikler', @SubId_0_2, N'magazinlikler', @RootId_0, 1),
(N'Maket Bıçağı', @SubId_0_2, N'maket-bicagi', @RootId_0, 1),
(N'Cetveller', @SubId_0_2, N'cetveller', @RootId_0, 1),
(N'Masaüstü Set ve Kalemlikler', @SubId_0_2, N'masaustu-set-ve-kalemlikler', @RootId_0, 1),
(N'Paket Lastikleri', @SubId_0_2, N'paket-lastikleri', @RootId_0, 1),
(N'Silgiler', @SubId_0_2, N'silgiler', @RootId_0, 1),
(N'Yapışkanlı Not Kağıtları', @SubId_0_2, N'yapiskanli-not-kagitlari', @RootId_0, 1),
(N'Zımba Teli / Zımba Sökücü', @SubId_0_2, N'zimba-teli-zimba-sokucu', @RootId_0, 1),
(N'Zımbalar', @SubId_0_2, N'zimbalar', @RootId_0, 1),
(N'Makas', @SubId_0_2, N'makas', @RootId_0, 1),
(N'Pul Süngeri', @SubId_0_2, N'pul-sungeri', @RootId_0, 1),
(N'Yapıştırıcı', @SubId_0_2, N'yapistirici', @RootId_0, 1),
(N'Küp Not Kağıtları', @SubId_0_2, N'kup-not-kagitlari', @RootId_0, 1);

-- Seviye 1: Pano ve Not Kağıdı
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Pano ve Not Kağıdı', @RootId_0, N'pano-ve-not-kagidi', @RootId_0, 1);
DECLARE @SubId_0_3 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Pano ve Not Kağıdı)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Flipchart Kağıdı', @SubId_0_3, N'flipchart-kagidi', @RootId_0, 1),
(N'Küp Not Kağıtları', @SubId_0_3, N'pano-ve-not-kagidi-kup-not-kagitlari', @RootId_0, 1),
(N'Yapışkanlı Not Kağıtları', @SubId_0_3, N'pano-ve-not-kagidi-yapiskanli-not-kagitlari', @RootId_0, 1);

-- Seviye 1: Okul Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Okul Ürünleri', @RootId_0, N'okul-urunleri', @RootId_0, 1);
DECLARE @SubId_0_4 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Okul Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Okul Çantaları', @SubId_0_4, N'okul-cantalari', @RootId_0, 1),
(N'Öğrenci Dosyaları', @SubId_0_4, N'ogrenci-dosyalari', @RootId_0, 1),
(N'Kurşun Kalemler', @SubId_0_4, N'okul-urunleri-kursun-kalemler', @RootId_0, 1),
(N'Oyun Hamuru', @SubId_0_4, N'oyun-hamuru', @RootId_0, 1),
(N'El İşi Malzemeleri', @SubId_0_4, N'el-isi-malzemeleri', @RootId_0, 1),
(N'Cetveller', @SubId_0_4, N'okul-urunleri-cetveller', @RootId_0, 1),
(N'Tebeşir', @SubId_0_4, N'tebesir', @RootId_0, 1),
(N'Bayrak ve Flama', @SubId_0_4, N'bayrak-ve-flama', @RootId_0, 1),
(N'Ataşlar', @SubId_0_4, N'okul-urunleri-ataslar', @RootId_0, 1),
(N'Boyama Ürünleri', @SubId_0_4, N'boyama-urunleri', @RootId_0, 1);

-- Seviye 1: Sunum ve Planlama
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Sunum ve Planlama', @RootId_0, N'sunum-ve-planlama', @RootId_0, 1);
DECLARE @SubId_0_5 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Sunum ve Planlama)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Cilt Kapakları', @SubId_0_5, N'cilt-kapaklari', @RootId_0, 1),
(N'Flipchart Kağıdı', @SubId_0_5, N'sunum-ve-planlama-flipchart-kagidi', @RootId_0, 1),
(N'Bayrak ve Flama', @SubId_0_5, N'sunum-ve-planlama-bayrak-ve-flama', @RootId_0, 1),
(N'Laminasyon Filmleri', @SubId_0_5, N'laminasyon-filmleri', @RootId_0, 1),
(N'Mantar Panolar', @SubId_0_5, N'mantar-panolar', @RootId_0, 1),
(N'Spiraller ve Üçgen Profiller', @SubId_0_5, N'spiraller-ve-ucgen-profiller', @RootId_0, 1),
(N'Tahta Silgisi', @SubId_0_5, N'tahta-silgisi', @RootId_0, 1),
(N'Yazı Tahtaları', @SubId_0_5, N'yazi-tahtalari', @RootId_0, 1);

-- Seviye 1: Dosyalama ve Arşivleme
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Dosyalama ve Arşivleme', @RootId_0, N'dosyalama-ve-arsivleme', @RootId_0, 1);
DECLARE @SubId_0_6 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Dosyalama ve Arşivleme)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Plastik Klasörler', @SubId_0_6, N'plastik-klasorler', @RootId_0, 1),
(N'Poşet Dosyalar', @SubId_0_6, N'poset-dosyalar', @RootId_0, 1),
(N'Arşiv Kutusu', @SubId_0_6, N'arsiv-kutusu', @RootId_0, 1),
(N'Askılı Dosyalar', @SubId_0_6, N'askili-dosyalar', @RootId_0, 1),
(N'Dosya Ayracı', @SubId_0_6, N'dosya-ayraci', @RootId_0, 1),
(N'Evrak Dosyası', @SubId_0_6, N'evrak-dosyasi', @RootId_0, 1),
(N'Sıkıştırmalı Dosya', @SubId_0_6, N'sikistirmali-dosya', @RootId_0, 1),
(N'Şömiz', @SubId_0_6, N'somiz', @RootId_0, 1),
(N'Karton Klasörler', @SubId_0_6, N'karton-klasorler', @RootId_0, 1),
(N'Sekreterlikler', @SubId_0_6, N'sekreterlikler', @RootId_0, 1),
(N'Sunum Dosyaları', @SubId_0_6, N'sunum-dosyalari', @RootId_0, 1),
(N'Tanıtım Klasörleri', @SubId_0_6, N'tanitim-klasorleri', @RootId_0, 1),
(N'Telli Dosyalar', @SubId_0_6, N'telli-dosyalar', @RootId_0, 1);

-- Seviye 1: Gönderi ve Paketleme
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Gönderi ve Paketleme', @RootId_0, N'gonderi-ve-paketleme', @RootId_0, 1);
DECLARE @SubId_0_7 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Gönderi ve Paketleme)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Poşet Çeşitleri', @SubId_0_7, N'poset-cesitleri', @RootId_0, 1),
(N'Kilitli Poşet', @SubId_0_7, N'kilitli-poset', @RootId_0, 1),
(N'Balonlu Naylon', @SubId_0_7, N'balonlu-naylon', @RootId_0, 1),
(N'Barkod Etiketleri', @SubId_0_7, N'barkod-etiketleri', @RootId_0, 1),
(N'Beyaz Etiketler', @SubId_0_7, N'beyaz-etiketler', @RootId_0, 1),
(N'Koli Bandı', @SubId_0_7, N'koli-bandi', @RootId_0, 1),
(N'Koli Bandı Makinesi', @SubId_0_7, N'koli-bandi-makinesi', @RootId_0, 1),
(N'Taşıma Kolisi', @SubId_0_7, N'tasima-kolisi', @RootId_0, 1),
(N'Kargo Ürünleri', @SubId_0_7, N'kargo-urunleri', @RootId_0, 1),
(N'Marker Kalem', @SubId_0_7, N'gonderi-ve-paketleme-marker-kalem', @RootId_0, 1),
(N'Palet Streç Film', @SubId_0_7, N'palet-strec-film', @RootId_0, 1),
(N'Paket Lastikleri', @SubId_0_7, N'gonderi-ve-paketleme-paket-lastikleri', @RootId_0, 1),
(N'Kılçık Tabancası ve Malzemeleri', @SubId_0_7, N'kilcik-tabancasi-ve-malzemeleri', @RootId_0, 1),
(N'Lojistik Etiketleri', @SubId_0_7, N'lojistik-etiketleri', @RootId_0, 1);

-- Seviye 1: Yazıcı ve Fotokopi Kağıtları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Yazıcı ve Fotokopi Kağıtları', @RootId_0, N'yazici-ve-fotokopi-kagitlari', @RootId_0, 1);
DECLARE @SubId_0_8 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Yazıcı ve Fotokopi Kağıtları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Fotokopi Kağıtları', @SubId_0_8, N'fotokopi-kagitlari', @RootId_0, 1),
(N'Numaralı Kağıt', @SubId_0_8, N'numarali-kagit', @RootId_0, 1),
(N'Fotoğraf Kağıtları', @SubId_0_8, N'fotograf-kagitlari', @RootId_0, 1),
(N'Gramajlı Fotokopi Kağıtları', @SubId_0_8, N'gramajli-fotokopi-kagitlari', @RootId_0, 1),
(N'Renkli Fotokopi Kağıtları', @SubId_0_8, N'renkli-fotokopi-kagitlari', @RootId_0, 1),
(N'Sürekli Form Kağıtları', @SubId_0_8, N'surekli-form-kagitlari', @RootId_0, 1);

-- Seviye 1: Teknik ve Özel Kağıtlar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Teknik ve Özel Kağıtlar', @RootId_0, N'teknik-ve-ozel-kagitlar', @RootId_0, 1);
DECLARE @SubId_0_9 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Teknik ve Özel Kağıtlar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Karbon Kağıdı', @SubId_0_9, N'karbon-kagidi', @RootId_0, 1),
(N'Plotter Kağıtları', @SubId_0_9, N'plotter-kagitlari', @RootId_0, 1);

-- Seviye 1: Termal Rulolar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Termal Rulolar', @RootId_0, N'termal-rulolar', @RootId_0, 1);
DECLARE @SubId_0_10 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Termal Rulolar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Yazarkasa ve Pos Ruloları', @SubId_0_10, N'yazarkasa-ve-pos-rulolari', @RootId_0, 1);

-- Seviye 1: Zarflar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Zarflar', @RootId_0, N'zarflar', @RootId_0, 1);
DECLARE @SubId_0_11 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Zarflar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'CD/DVD ve Mektup Zarfları', @SubId_0_11, N'cddvd-ve-mektup-zarflari', @RootId_0, 1),
(N'Diplomat ve Para Zarfları', @SubId_0_11, N'diplomat-ve-para-zarflari', @RootId_0, 1),
(N'Hava Kabarcıklı Zarflar', @SubId_0_11, N'hava-kabarcikli-zarflar', @RootId_0, 1),
(N'Torba Zarflar', @SubId_0_11, N'torba-zarflar', @RootId_0, 1);

-- Seviye 1: Etiketler ve Etiket Makinaları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Etiketler ve Etiket Makinaları', @RootId_0, N'etiketler-ve-etiket-makinalari', @RootId_0, 1);
DECLARE @SubId_0_12 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Etiketler ve Etiket Makinaları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Barkod ve Etiket Yazıcı', @SubId_0_12, N'barkod-ve-etiket-yazici', @RootId_0, 1),
(N'Barkod Etiketleri', @SubId_0_12, N'etiketler-ve-etiket-makinalari-barkod-etiketleri', @RootId_0, 1),
(N'Beyaz Etiketler', @SubId_0_12, N'etiketler-ve-etiket-makinalari-beyaz-etiketler', @RootId_0, 1),
(N'Etiket Makinesi', @SubId_0_12, N'etiket-makinesi', @RootId_0, 1),
(N'Etiket Makinesi Etiketleri', @SubId_0_12, N'etiket-makinesi-etiketleri', @RootId_0, 1),
(N'Nokta Etiketleri', @SubId_0_12, N'nokta-etiketleri', @RootId_0, 1),
(N'Lojistik Etiketleri', @SubId_0_12, N'etiketler-ve-etiket-makinalari-lojistik-etiketleri', @RootId_0, 1),
(N'Renkli Laser Etiketler', @SubId_0_12, N'renkli-laser-etiketler', @RootId_0, 1),
(N'Adres Etiketi', @SubId_0_12, N'adres-etiketi', @RootId_0, 1),
(N'Şeffaf Etiket', @SubId_0_12, N'seffaf-etiket', @RootId_0, 1),
(N'Raf Etiketleri', @SubId_0_12, N'raf-etiketleri', @RootId_0, 1);

GO

-- ============================================================================
-- SEKTÖR 2: TEMIZLIK VE SAĞLıK
-- ============================================================================
DECLARE @RootId_1 INT = (SELECT Id FROM dbo.Categories WHERE Slug = N'temizlik-ve-saglik' AND ParentCategoryId IS NULL);

-- Seviye 1: Kağıt Temizlik Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kağıt Temizlik Ürünleri', @RootId_1, N'kagit-temizlik-urunleri', @RootId_1, 1);
DECLARE @SubId_1_0 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kağıt Temizlik Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Kağıt Havlular', @SubId_1_0, N'kagit-havlular', @RootId_1, 1),
(N'Tuvalet Kağıtları', @SubId_1_0, N'tuvalet-kagitlari', @RootId_1, 1),
(N'Islak Mendiller', @SubId_1_0, N'islak-mendiller', @RootId_1, 1),
(N'Peçete ve Dispenserleri', @SubId_1_0, N'pecete-ve-dispenserleri', @RootId_1, 1),
(N'Kağıt Havlu Dispenserleri', @SubId_1_0, N'kagit-havlu-dispenserleri', @RootId_1, 1),
(N'Tuvalet Kağıdı Dispenserleri', @SubId_1_0, N'tuvalet-kagidi-dispenserleri', @RootId_1, 1),
(N'Klozet Kapak Örtüsü ve Dispenseri', @SubId_1_0, N'klozet-kapak-ortusu-ve-dispenseri', @RootId_1, 1),
(N'Muayene Masa Örtüsü', @SubId_1_0, N'muayene-masa-ortusu', @RootId_1, 1);

-- Seviye 1: Endüstriyel Temizlik Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Endüstriyel Temizlik Ürünleri', @RootId_1, N'endustriyel-temizlik-urunleri', @RootId_1, 1);
DECLARE @SubId_1_1 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Endüstriyel Temizlik Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Profesyonel Kimyasallar & Endüstriyel Deterjanlar', @SubId_1_1, N'profesyonel-kimyasallar-ve-endustriyel-deterjanlar', @RootId_1, 1),
(N'Endüstriyel Temizlik Ekipmanları', @SubId_1_1, N'endustriyel-temizlik-ekipmanlari', @RootId_1, 1),
(N'Endüstriyel Mutfak Malzemeleri', @SubId_1_1, N'endustriyel-mutfak-malzemeleri', @RootId_1, 1),
(N'Endüstriyel Atık Yönetimi', @SubId_1_1, N'endustriyel-atik-yonetimi', @RootId_1, 1),
(N'Endüstriyel Dispenser ve Aparatlar', @SubId_1_1, N'endustriyel-dispenser-ve-aparatlar', @RootId_1, 1);

-- Seviye 1: Tuvalet - Banyo Temizliği
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Tuvalet - Banyo Temizliği', @RootId_1, N'tuvalet-banyo-temizligi', @RootId_1, 1);
DECLARE @SubId_1_2 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Tuvalet - Banyo Temizliği)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Çöp Torbaları', @SubId_1_2, N'cop-torbalari', @RootId_1, 1),
(N'Kağıt Havlular', @SubId_1_2, N'tuvalet-banyo-temizligi-kagit-havlular', @RootId_1, 1),
(N'Kağıt Havlu Dispenserleri', @SubId_1_2, N'tuvalet-banyo-temizligi-kagit-havlu-dispenserleri', @RootId_1, 1),
(N'Klozet Koku Gidericiler', @SubId_1_2, N'klozet-koku-gidericiler', @RootId_1, 1),
(N'Klozet Kapak Örtüsü ve Dispenseri', @SubId_1_2, N'tuvalet-banyo-temizligi-klozet-kapak-ortusu-ve-dispenseri', @RootId_1, 1),
(N'Sıvı Sabunluk', @SubId_1_2, N'sivi-sabunluk', @RootId_1, 1),
(N'Sabunlar', @SubId_1_2, N'sabunlar', @RootId_1, 1),
(N'Temizlik Bezleri', @SubId_1_2, N'temizlik-bezleri', @RootId_1, 1),
(N'Tuvalet Kağıtları', @SubId_1_2, N'tuvalet-banyo-temizligi-tuvalet-kagitlari', @RootId_1, 1),
(N'Tuvalet Kağıdı Dispenserleri', @SubId_1_2, N'tuvalet-banyo-temizligi-tuvalet-kagidi-dispenserleri', @RootId_1, 1),
(N'El Kurutma Makinesi', @SubId_1_2, N'el-kurutma-makinesi', @RootId_1, 1);

-- Seviye 1: Ofis Temizlik Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Ofis Temizlik Ürünleri', @RootId_1, N'ofis-temizlik-urunleri', @RootId_1, 1);
DECLARE @SubId_1_3 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Ofis Temizlik Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Çamaşır Suları', @SubId_1_3, N'camasir-sulari', @RootId_1, 1),
(N'Çöp Kovaları', @SubId_1_3, N'cop-kovalari', @RootId_1, 1),
(N'Çöp Torbaları', @SubId_1_3, N'ofis-temizlik-urunleri-cop-torbalari', @RootId_1, 1),
(N'Oda Kokuları', @SubId_1_3, N'oda-kokulari', @RootId_1, 1),
(N'Temizlik Bezleri', @SubId_1_3, N'ofis-temizlik-urunleri-temizlik-bezleri', @RootId_1, 1),
(N'Temizlik Mopları ve Yedekleri', @SubId_1_3, N'temizlik-moplari-ve-yedekleri', @RootId_1, 1),
(N'El Dezenfektanları', @SubId_1_3, N'el-dezenfektanlari', @RootId_1, 1),
(N'Böcek İlaçları', @SubId_1_3, N'bocek-ilaclari', @RootId_1, 1);

-- Seviye 1: Çamaşır Deterjanı
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Çamaşır Deterjanı', @RootId_1, N'camasir-deterjani', @RootId_1, 1);
DECLARE @SubId_1_4 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Çamaşır Deterjanı)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Sıvı Deterjan', @SubId_1_4, N'sivi-deterjan', @RootId_1, 1),
(N'Toz Deterjan', @SubId_1_4, N'toz-deterjan', @RootId_1, 1),
(N'Yumuşatıcı', @SubId_1_4, N'yumusatici', @RootId_1, 1);

-- Seviye 1: Leke Çıkarıcı
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Leke Çıkarıcı', @RootId_1, N'leke-cikarici', @RootId_1, 1);
DECLARE @SubId_1_5 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Leke Çıkarıcı)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Kireç Çözücü', @SubId_1_5, N'kirec-cozucu', @RootId_1, 1),
(N'Yağ Sökücü', @SubId_1_5, N'yag-sokucu', @RootId_1, 1),
(N'Lavabo Açıcı', @SubId_1_5, N'lavabo-acici', @RootId_1, 1);

-- Seviye 1: Yardımcı Temizlik Gereçleri ve Yedekleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Yardımcı Temizlik Gereçleri ve Yedekleri', @RootId_1, N'yardimci-temizlik-gerecleri-ve-yedekleri', @RootId_1, 1);
DECLARE @SubId_1_6 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Yardımcı Temizlik Gereçleri ve Yedekleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Temizlik Seti', @SubId_1_6, N'temizlik-seti', @RootId_1, 1),
(N'Mop Sapı', @SubId_1_6, N'mop-sapi', @RootId_1, 1),
(N'Temizlik Kovası', @SubId_1_6, N'temizlik-kovasi', @RootId_1, 1),
(N'Mop', @SubId_1_6, N'mop', @RootId_1, 1),
(N'Mop Aparatı', @SubId_1_6, N'mop-aparati', @RootId_1, 1),
(N'Tuvalet Fırçası', @SubId_1_6, N'tuvalet-fircasi', @RootId_1, 1),
(N'Tüy Toplayıcı', @SubId_1_6, N'tuy-toplayici', @RootId_1, 1),
(N'Çekçek', @SubId_1_6, N'cekcek', @RootId_1, 1),
(N'Cam Sileceği', @SubId_1_6, N'cam-silecegi', @RootId_1, 1),
(N'Temizlik Fırçası', @SubId_1_6, N'temizlik-fircasi', @RootId_1, 1),
(N'Faraş', @SubId_1_6, N'faras', @RootId_1, 1),
(N'Yüzey Kazıma Aparatı', @SubId_1_6, N'yuzey-kazima-aparati', @RootId_1, 1);

-- Seviye 1: Yüzey Temizlik Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Yüzey Temizlik Ürünleri', @RootId_1, N'yuzey-temizlik-urunleri', @RootId_1, 1);
DECLARE @SubId_1_7 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Yüzey Temizlik Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Yüzey Temizleyici', @SubId_1_7, N'yuzey-temizleyici', @RootId_1, 1),
(N'Arap Sabunu', @SubId_1_7, N'arap-sabunu', @RootId_1, 1),
(N'Cam Temizleyiciler', @SubId_1_7, N'cam-temizleyiciler', @RootId_1, 1),
(N'Ahşap Temizleyici', @SubId_1_7, N'ahsap-temizleyici', @RootId_1, 1),
(N'Mutfak ve Banyo Temizleyicileri', @SubId_1_7, N'mutfak-ve-banyo-temizleyicileri', @RootId_1, 1),
(N'Yüzey Temizleme Havlusu', @SubId_1_7, N'yuzey-temizleme-havlusu', @RootId_1, 1);

-- Seviye 1: Ağız Bakım Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Ağız Bakım Ürünleri', @RootId_1, N'agiz-bakim-urunleri', @RootId_1, 1);
DECLARE @SubId_1_8 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Ağız Bakım Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Diş Macunu', @SubId_1_8, N'dis-macunu', @RootId_1, 1),
(N'Diş Fırçası', @SubId_1_8, N'dis-fircasi', @RootId_1, 1),
(N'Diğer Ağız Bakım Ürünleri', @SubId_1_8, N'diger-agiz-bakim-urunleri', @RootId_1, 1);

-- Seviye 1: Kişisel Bakım Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kişisel Bakım Ürünleri', @RootId_1, N'kisisel-bakim-urunleri', @RootId_1, 1);
DECLARE @SubId_1_9 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kişisel Bakım Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Islak Mendiller', @SubId_1_9, N'kisisel-bakim-urunleri-islak-mendiller', @RootId_1, 1),
(N'Kolonya', @SubId_1_9, N'kolonya', @RootId_1, 1),
(N'Kişisel Hijyen Ürünleri', @SubId_1_9, N'kisisel-hijyen-urunleri', @RootId_1, 1),
(N'El Dezenfektanları', @SubId_1_9, N'kisisel-bakim-urunleri-el-dezenfektanlari', @RootId_1, 1),
(N'Tıraş Bıçağı', @SubId_1_9, N'tiras-bicagi', @RootId_1, 1);

-- Seviye 1: Mutfak Temizlik Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Mutfak Temizlik Ürünleri', @RootId_1, N'mutfak-temizlik-urunleri', @RootId_1, 1);
DECLARE @SubId_1_10 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Mutfak Temizlik Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Çöp Torbaları', @SubId_1_10, N'mutfak-temizlik-urunleri-cop-torbalari', @RootId_1, 1),
(N'Kağıt Havlular', @SubId_1_10, N'mutfak-temizlik-urunleri-kagit-havlular', @RootId_1, 1),
(N'Mutfak ve Tezgah Temizliği', @SubId_1_10, N'mutfak-ve-tezgah-temizligi', @RootId_1, 1),
(N'Peçete ve Dispenserleri', @SubId_1_10, N'mutfak-temizlik-urunleri-pecete-ve-dispenserleri', @RootId_1, 1),
(N'Bulaşık Süngeri', @SubId_1_10, N'bulasik-sungeri', @RootId_1, 1),
(N'Bulaşık Teli', @SubId_1_10, N'bulasik-teli', @RootId_1, 1);

-- Seviye 1: Bulaşık Deterjanları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Bulaşık Deterjanları', @RootId_1, N'bulasik-deterjanlari', @RootId_1, 1);
DECLARE @SubId_1_11 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Bulaşık Deterjanları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Sıvı Bulaşık Deterjanı', @SubId_1_11, N'sivi-bulasik-deterjani', @RootId_1, 1),
(N'Bulaşık Makinesi Deterjanı', @SubId_1_11, N'bulasik-makinesi-deterjani', @RootId_1, 1),
(N'Parlatıcı', @SubId_1_11, N'parlatici', @RootId_1, 1),
(N'Bulaşık Makinesi Tuzu', @SubId_1_11, N'bulasik-makinesi-tuzu', @RootId_1, 1);

-- Seviye 1: Eldivenler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Eldivenler', @RootId_1, N'eldivenler', @RootId_1, 1);
DECLARE @SubId_1_12 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Eldivenler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Bulaşık ve Temizlik Eldiveni', @SubId_1_12, N'bulasik-ve-temizlik-eldiveni', @RootId_1, 1),
(N'Muayene Eldiveni', @SubId_1_12, N'muayene-eldiveni', @RootId_1, 1);

GO

-- ============================================================================
-- SEKTÖR 3: GıDA VE MUTFAK
-- ============================================================================
DECLARE @RootId_2 INT = (SELECT Id FROM dbo.Categories WHERE Slug = N'gida-ve-mutfak' AND ParentCategoryId IS NULL);

-- Seviye 1: Temel Gıda Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Temel Gıda Ürünleri', @RootId_2, N'temel-gida-urunleri', @RootId_2, 1);
DECLARE @SubId_2_0 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Temel Gıda Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Kahvaltılık', @SubId_2_0, N'kahvaltilik', @RootId_2, 1),
(N'Kuru Bakliyat', @SubId_2_0, N'kuru-bakliyat', @RootId_2, 1),
(N'Makarna', @SubId_2_0, N'makarna', @RootId_2, 1),
(N'Sıvı Yağ', @SubId_2_0, N'sivi-yag', @RootId_2, 1),
(N'Tuz ve Baharat', @SubId_2_0, N'tuz-ve-baharat', @RootId_2, 1),
(N'Kahvaltılık Gevrek', @SubId_2_0, N'kahvaltilik-gevrek', @RootId_2, 1),
(N'Ramazan Kolileri', @SubId_2_0, N'ramazan-kolileri', @RootId_2, 1),
(N'Bayram İkramlıkları', @SubId_2_0, N'bayram-ikramliklari', @RootId_2, 1);

-- Seviye 1: Şekerler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Şekerler', @RootId_2, N'sekerler', @RootId_2, 1);
DECLARE @SubId_2_1 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Şekerler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Toz Şekerler', @SubId_2_1, N'toz-sekerler', @RootId_2, 1),
(N'Küp Şekerler', @SubId_2_1, N'kup-sekerler', @RootId_2, 1),
(N'Stick Şeker', @SubId_2_1, N'stick-seker', @RootId_2, 1);

-- Seviye 1: Çaylar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Çaylar', @RootId_2, N'caylar', @RootId_2, 1);
DECLARE @SubId_2_2 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Çaylar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Bardak Poşet Çaylar', @SubId_2_2, N'bardak-poset-caylar', @RootId_2, 1),
(N'Bitki Çayları', @SubId_2_2, N'bitki-caylari', @RootId_2, 1),
(N'Demlik Poşet Çaylar', @SubId_2_2, N'demlik-poset-caylar', @RootId_2, 1),
(N'Dökme Çaylar', @SubId_2_2, N'dokme-caylar', @RootId_2, 1),
(N'Meyve Çayı', @SubId_2_2, N'meyve-cayi', @RootId_2, 1);

-- Seviye 1: İçecekler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'İçecekler', @RootId_2, N'icecekler', @RootId_2, 1);
DECLARE @SubId_2_3 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (İçecekler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Gazlı İçecekler', @SubId_2_3, N'gazli-icecekler', @RootId_2, 1),
(N'Meyve Suları', @SubId_2_3, N'meyve-sulari', @RootId_2, 1),
(N'Sıcak Çikolata', @SubId_2_3, N'sicak-cikolata', @RootId_2, 1),
(N'Su', @SubId_2_3, N'su', @RootId_2, 1),
(N'Soda ve Maden Suyu', @SubId_2_3, N'soda-ve-maden-suyu', @RootId_2, 1),
(N'Sütler', @SubId_2_3, N'sutler', @RootId_2, 1),
(N'Enerji İçeceği', @SubId_2_3, N'enerji-icecegi', @RootId_2, 1),
(N'Soğuk Çaylar', @SubId_2_3, N'soguk-caylar', @RootId_2, 1),
(N'Toz İçecek', @SubId_2_3, N'toz-icecek', @RootId_2, 1),
(N'Soğuk Kahveler', @SubId_2_3, N'soguk-kahveler', @RootId_2, 1);

-- Seviye 1: Kahveler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kahveler', @RootId_2, N'kahveler', @RootId_2, 1);
DECLARE @SubId_2_4 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kahveler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Türk Kahvesi', @SubId_2_4, N'turk-kahvesi', @RootId_2, 1),
(N'Filtre Kahveler', @SubId_2_4, N'filtre-kahveler', @RootId_2, 1),
(N'Hazır Kahveler', @SubId_2_4, N'hazir-kahveler', @RootId_2, 1),
(N'Süt Tozu ve Kahve Kreması', @SubId_2_4, N'sut-tozu-ve-kahve-kremasi', @RootId_2, 1),
(N'Çekirdek Kahveler', @SubId_2_4, N'cekirdek-kahveler', @RootId_2, 1),
(N'Kapsül Kahveler', @SubId_2_4, N'kapsul-kahveler', @RootId_2, 1),
(N'Espresso Kahve', @SubId_2_4, N'espresso-kahve', @RootId_2, 1),
(N'Kahve Makineleri', @SubId_2_4, N'kahve-makineleri', @RootId_2, 1),
(N'Filtre Kahve Kağıdı', @SubId_2_4, N'filtre-kahve-kagidi', @RootId_2, 1),
(N'Kahve Şurupları', @SubId_2_4, N'kahve-suruplari', @RootId_2, 1);

-- Seviye 1: Kullan At Mutfak Malzemeleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kullan At Mutfak Malzemeleri', @RootId_2, N'kullan-at-mutfak-malzemeleri', @RootId_2, 1);
DECLARE @SubId_2_5 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kullan At Mutfak Malzemeleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Gıda Ambalaj Malzemeleri', @SubId_2_5, N'gida-ambalaj-malzemeleri', @RootId_2, 1),
(N'Karıştırıcılar', @SubId_2_5, N'karistiricilar', @RootId_2, 1),
(N'Karton Bardaklar', @SubId_2_5, N'karton-bardaklar', @RootId_2, 1),
(N'Kullan At Kaşık - Çatal ve Bıçaklar', @SubId_2_5, N'kullan-at-kasik-catal-ve-bicaklar', @RootId_2, 1),
(N'Kullan At Tabaklar', @SubId_2_5, N'kullan-at-tabaklar', @RootId_2, 1),
(N'Peçete ve Dispenserleri', @SubId_2_5, N'pecete-ve-dispenserleri', @RootId_2, 1),
(N'Plastik Bardaklar', @SubId_2_5, N'plastik-bardaklar', @RootId_2, 1);

-- Seviye 1: Atıştırmalıklar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Atıştırmalıklar', @RootId_2, N'atistirmaliklar', @RootId_2, 1);
DECLARE @SubId_2_6 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Atıştırmalıklar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Bisküvi ve Kekler', @SubId_2_6, N'biskuvi-ve-kekler', @RootId_2, 1),
(N'Diyet Ürünler', @SubId_2_6, N'diyet-urunler', @RootId_2, 1),
(N'Gofret ve Çikolatalar', @SubId_2_6, N'gofret-ve-cikolatalar', @RootId_2, 1),
(N'Kuruyemiş', @SubId_2_6, N'kuruyemis', @RootId_2, 1),
(N'Tuzlu Atıştırmalıklar', @SubId_2_6, N'tuzlu-atistirmaliklar', @RootId_2, 1),
(N'Bayram İkramlıkları', @SubId_2_6, N'atistirmaliklar-bayram-ikramliklari', @RootId_2, 1);

-- Seviye 1: Mutfak Malzemeleri ve Gereçleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Mutfak Malzemeleri ve Gereçleri', @RootId_2, N'mutfak-malzemeleri-ve-gerecleri', @RootId_2, 1);
DECLARE @SubId_2_7 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Mutfak Malzemeleri ve Gereçleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Çay Bardakları', @SubId_2_7, N'cay-bardaklari', @RootId_2, 1),
(N'Kahve Bardakları', @SubId_2_7, N'kahve-bardaklari', @RootId_2, 1),
(N'Saklama Kapları', @SubId_2_7, N'saklama-kaplari', @RootId_2, 1),
(N'Su Bardağı ve Sürahiler', @SubId_2_7, N'su-bardagi-ve-surahiler', @RootId_2, 1),
(N'Kaşık - Çatal ve Bıçaklar', @SubId_2_7, N'kasik-catal-ve-bicaklar', @RootId_2, 1),
(N'Termoslar', @SubId_2_7, N'termoslar', @RootId_2, 1);

-- Seviye 1: Kahve Makineleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kahve Makineleri', @RootId_2, N'gida-ve-mutfak-kahve-makineleri', @RootId_2, 1);
DECLARE @SubId_2_8 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kahve Makineleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Türk Kahvesi Makinesi', @SubId_2_8, N'turk-kahvesi-makinesi', @RootId_2, 1),
(N'Filtre Kahve Makinesi', @SubId_2_8, N'filtre-kahve-makinesi', @RootId_2, 1),
(N'Kapsül Kahve Makinesi', @SubId_2_8, N'kapsul-kahve-makinesi', @RootId_2, 1),
(N'Otomatik Kahve Makinesi', @SubId_2_8, N'otomatik-kahve-makinesi', @RootId_2, 1),
(N'Manuel Espresso Makinesi', @SubId_2_8, N'manuel-espresso-makinesi', @RootId_2, 1),
(N'Elektrikli Cezve', @SubId_2_8, N'elektrikli-cezve', @RootId_2, 1);

-- Seviye 1: Küçük Ev Aletleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Küçük Ev Aletleri', @RootId_2, N'kucuk-ev-aletleri', @RootId_2, 1);
DECLARE @SubId_2_9 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Küçük Ev Aletleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Çay Makinesi', @SubId_2_9, N'cay-makinesi', @RootId_2, 1),
(N'Su Isıtıcıları', @SubId_2_9, N'su-isiticilari', @RootId_2, 1),
(N'Su Sebilleri', @SubId_2_9, N'su-sebilleri', @RootId_2, 1),
(N'Tost Makinesi', @SubId_2_9, N'tost-makinesi', @RootId_2, 1),
(N'Fırın', @SubId_2_9, N'firin', @RootId_2, 1),
(N'Elektrikli Ocak', @SubId_2_9, N'elektrikli-ocak', @RootId_2, 1),
(N'Buzdolabı', @SubId_2_9, N'buzdolabi', @RootId_2, 1);

GO

-- ============================================================================
-- SEKTÖR 4: KARTUŞ - TONER
-- ============================================================================
DECLARE @RootId_3 INT = (SELECT Id FROM dbo.Categories WHERE Slug = N'kartus-toner' AND ParentCategoryId IS NULL);

-- Seviye 1: Kartuşlar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kartuşlar', @RootId_3, N'kartuslar', @RootId_3, 1);
DECLARE @SubId_3_0 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kartuşlar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Brother Kartuş', @SubId_3_0, N'brother-kartus', @RootId_3, 1),
(N'Canon Kartuş', @SubId_3_0, N'canon-kartus', @RootId_3, 1),
(N'Epson Mürekkep', @SubId_3_0, N'epson-murekkep', @RootId_3, 1),
(N'HP Kartuş', @SubId_3_0, N'hp-kartus', @RootId_3, 1);

-- Seviye 1: 3D Yazıcı Filament
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'3D Yazıcı Filament', @RootId_3, N'3d-yazici-filament', @RootId_3, 1);
DECLARE @SubId_3_1 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (3D Yazıcı Filament)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'PLA Filament', @SubId_3_1, N'pla-filament', @RootId_3, 1);

-- Seviye 1: Drumlar ve Şeritler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Drumlar ve Şeritler', @RootId_3, N'drumlar-ve-seritler', @RootId_3, 1);
DECLARE @SubId_3_2 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Drumlar ve Şeritler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Drumlar', @SubId_3_2, N'drumlar', @RootId_3, 1),
(N'Oki Şerit', @SubId_3_2, N'oki-serit', @RootId_3, 1),
(N'Epson Şerit', @SubId_3_2, N'epson-serit', @RootId_3, 1);

-- Seviye 1: Tonerler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Tonerler', @RootId_3, N'tonerler', @RootId_3, 1);
DECLARE @SubId_3_3 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Tonerler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Canon Toner', @SubId_3_3, N'canon-toner', @RootId_3, 1),
(N'HP Toner', @SubId_3_3, N'hp-toner', @RootId_3, 1),
(N'Oki Toner', @SubId_3_3, N'oki-toner', @RootId_3, 1),
(N'Brother Toner', @SubId_3_3, N'brother-toner', @RootId_3, 1),
(N'Konica Minolta Toner', @SubId_3_3, N'konica-minolta-toner', @RootId_3, 1);

-- Seviye 1: Muadil Tonerler
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Muadil Tonerler', @RootId_3, N'muadil-tonerler', @RootId_3, 1);
DECLARE @SubId_3_4 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Muadil Tonerler)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Samsung Muadil Toner', @SubId_3_4, N'samsung-muadil-toner', @RootId_3, 1),
(N'Xerox Muadil Toner', @SubId_3_4, N'xerox-muadil-toner', @RootId_3, 1),
(N'HP Muadil Toner', @SubId_3_4, N'hp-muadil-toner', @RootId_3, 1),
(N'Canon Muadil Toner', @SubId_3_4, N'canon-muadil-toner', @RootId_3, 1),
(N'Brother Muadil Toner', @SubId_3_4, N'brother-muadil-toner', @RootId_3, 1),
(N'Pantum Muadil Toner', @SubId_3_4, N'pantum-muadil-toner', @RootId_3, 1),
(N'Lexmark Muadil Toner', @SubId_3_4, N'lexmark-muadil-toner', @RootId_3, 1),
(N'Kyocera Muadil Toner', @SubId_3_4, N'kyocera-muadil-toner', @RootId_3, 1);

GO

-- ============================================================================
-- SEKTÖR 5: MOBILYA VE HıRDAVAT
-- ============================================================================
DECLARE @RootId_4 INT = (SELECT Id FROM dbo.Categories WHERE Slug = N'mobilya-ve-hirdavat' AND ParentCategoryId IS NULL);

-- Seviye 1: Ofis Mobilyaları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Ofis Mobilyaları', @RootId_4, N'ofis-mobilyalari', @RootId_4, 1);
DECLARE @SubId_4_0 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Ofis Mobilyaları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Dolap ve Kitaplık', @SubId_4_0, N'dolap-ve-kitaplik', @RootId_4, 1),
(N'Çalışma ve Ofis Sandalyesi', @SubId_4_0, N'calisma-ve-ofis-sandalyesi', @RootId_4, 1),
(N'Çalışma ve Ofis Masası', @SubId_4_0, N'calisma-ve-ofis-masasi', @RootId_4, 1),
(N'Ofis Tipi Askılıklar', @SubId_4_0, N'ofis-tipi-askiliklar', @RootId_4, 1),
(N'Keson', @SubId_4_0, N'keson', @RootId_4, 1),
(N'Ortopedik Ofis Ürünleri', @SubId_4_0, N'ortopedik-ofis-urunleri', @RootId_4, 1);

-- Seviye 1: Kamp ve Bahçe Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kamp ve Bahçe Ürünleri', @RootId_4, N'kamp-ve-bahce-urunleri', @RootId_4, 1);
DECLARE @SubId_4_1 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kamp ve Bahçe Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Bahçe ve Sulama Ürünleri', @SubId_4_1, N'bahce-ve-sulama-urunleri', @RootId_4, 1),
(N'Kamp Sandalyeleri ve Malzemeleri', @SubId_4_1, N'kamp-sandalyeleri-ve-malzemeleri', @RootId_4, 1);

-- Seviye 1: Ofis Aksesuarları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Ofis Aksesuarları', @RootId_4, N'ofis-aksesuarlari', @RootId_4, 1);
DECLARE @SubId_4_2 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Ofis Aksesuarları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Bayrak ve Flama', @SubId_4_2, N'bayrak-ve-flama', @RootId_4, 1),
(N'Duvar ve Masa Saatleri', @SubId_4_2, N'duvar-ve-masa-saatleri', @RootId_4, 1),
(N'Küllükler', @SubId_4_2, N'kullukler', @RootId_4, 1),
(N'Paspas', @SubId_4_2, N'paspas', @RootId_4, 1),
(N'Dekoratif Masaüstü Ürünler', @SubId_4_2, N'dekoratif-masaustu-urunler', @RootId_4, 1);

-- Seviye 1: Aydınlatma ve Elektrik Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Aydınlatma ve Elektrik Ürünleri', @RootId_4, N'aydinlatma-ve-elektrik-urunleri', @RootId_4, 1);
DECLARE @SubId_4_3 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Aydınlatma ve Elektrik Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Ampuller', @SubId_4_3, N'ampuller', @RootId_4, 1),
(N'Işıldak ve Fenerler', @SubId_4_3, N'isildak-ve-fenerler', @RootId_4, 1),
(N'Uzatma Kabloları', @SubId_4_3, N'uzatma-kablolari', @RootId_4, 1),
(N'Elektrik Malzemeleri', @SubId_4_3, N'elektrik-malzemeleri', @RootId_4, 1),
(N'Masa Lambaları', @SubId_4_3, N'masa-lambalari', @RootId_4, 1);

-- Seviye 1: Hırdavat
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Hırdavat', @RootId_4, N'hirdavat', @RootId_4, 1);
DECLARE @SubId_4_4 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Hırdavat)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Elektrikli El Aletleri', @SubId_4_4, N'elektrikli-el-aletleri', @RootId_4, 1),
(N'Metre ve Ölçüm Aletleri', @SubId_4_4, N'metre-ve-olcum-aletleri', @RootId_4, 1),
(N'Alet Takım Çantası', @SubId_4_4, N'alet-takim-cantasi', @RootId_4, 1),
(N'El Aletleri', @SubId_4_4, N'el-aletleri', @RootId_4, 1),
(N'Maskeleme ve Çift Taraflı Bantlar', @SubId_4_4, N'maskeleme-ve-cift-tarafli-bantlar', @RootId_4, 1),
(N'Yük Taşıma Arabası', @SubId_4_4, N'yuk-tasima-arabasi', @RootId_4, 1),
(N'Merdivenler', @SubId_4_4, N'merdivenler', @RootId_4, 1),
(N'Özel ve Kaydırmaz Bantlar', @SubId_4_4, N'ozel-ve-kaydirmaz-bantlar', @RootId_4, 1),
(N'Balonlu Naylon', @SubId_4_4, N'balonlu-naylon', @RootId_4, 1),
(N'Palet Streç Film', @SubId_4_4, N'palet-strec-film', @RootId_4, 1),
(N'Taşıma Kolisi', @SubId_4_4, N'tasima-kolisi', @RootId_4, 1),
(N'Koli Bandı', @SubId_4_4, N'koli-bandi', @RootId_4, 1),
(N'Koli Bandı Makinesi', @SubId_4_4, N'koli-bandi-makinesi', @RootId_4, 1),
(N'Yağlayıcı ve Pas Çözücüler', @SubId_4_4, N'yaglayici-ve-pas-cozuculer', @RootId_4, 1),
(N'Yapıştırıcılar ve Silikonlar', @SubId_4_4, N'yapistiricilar-ve-silikonlar', @RootId_4, 1),
(N'Para Kasası', @SubId_4_4, N'para-kasasi', @RootId_4, 1),
(N'Asma Kilit ve Kapı Kilidi', @SubId_4_4, N'asma-kilit-ve-kapi-kilidi', @RootId_4, 1);

-- Seviye 1: İş Sağlığı ve Güvenliği
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'İş Sağlığı ve Güvenliği', @RootId_4, N'is-sagligi-ve-guvenligi', @RootId_4, 1);
DECLARE @SubId_4_5 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (İş Sağlığı ve Güvenliği)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'İş Ayakkabısı', @SubId_4_5, N'is-ayakkabisi', @RootId_4, 1),
(N'İş Güvenliği Malzemeleri', @SubId_4_5, N'is-guvenligi-malzemeleri', @RootId_4, 1),
(N'İlk Yardım Malzemeleri', @SubId_4_5, N'ilk-yardim-malzemeleri', @RootId_4, 1),
(N'Uyarı ve Yönlendirme Levhaları', @SubId_4_5, N'uyari-ve-yonlendirme-levhalari', @RootId_4, 1),
(N'Yaka Kartı ve Aksesuarları', @SubId_4_5, N'yaka-karti-ve-aksesuarlari', @RootId_4, 1),
(N'İş Eldiveni', @SubId_4_5, N'is-eldiveni', @RootId_4, 1),
(N'Emniyet Şeridi', @SubId_4_5, N'emniyet-seridi', @RootId_4, 1),
(N'Uyarı ve Yer İşaret Bandı', @SubId_4_5, N'uyari-ve-yer-isaret-bandi', @RootId_4, 1),
(N'Yangın Tüpü', @SubId_4_5, N'yangin-tupu', @RootId_4, 1),
(N'İş Elbisesi', @SubId_4_5, N'is-elbisesi', @RootId_4, 1);

-- Seviye 1: Otomobil ve Motosiklet Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Otomobil ve Motosiklet Ürünleri', @RootId_4, N'otomobil-ve-motosiklet-urunleri', @RootId_4, 1);
DECLARE @SubId_4_6 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Otomobil ve Motosiklet Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Araç Aksesuarları', @SubId_4_6, N'arac-aksesuarlari', @RootId_4, 1),
(N'Araç Ekipmanları', @SubId_4_6, N'arac-ekipmanlari', @RootId_4, 1),
(N'Araç Bakım ve Temizlik Ürünleri', @SubId_4_6, N'arac-bakim-ve-temizlik-urunleri', @RootId_4, 1),
(N'Kaza Tespit Tutanağı', @SubId_4_6, N'kaza-tespit-tutanagi', @RootId_4, 1);

GO

-- ============================================================================
-- SEKTÖR 6: TEKNOLOJI
-- ============================================================================
DECLARE @RootId_5 INT = (SELECT Id FROM dbo.Categories WHERE Slug = N'teknoloji' AND ParentCategoryId IS NULL);

-- Seviye 1: Ofis Teknolojisi
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Ofis Teknolojisi', @RootId_5, N'ofis-teknolojisi', @RootId_5, 1);
DECLARE @SubId_5_0 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Ofis Teknolojisi)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Barkod Okuyucular', @SubId_5_0, N'barkod-okuyucular', @RootId_5, 1),
(N'Ciltleme Makineleri', @SubId_5_0, N'ciltleme-makineleri', @RootId_5, 1),
(N'Etiket Makinesi', @SubId_5_0, N'etiket-makinesi', @RootId_5, 1),
(N'Kağıt Kesme Makinesi', @SubId_5_0, N'kagit-kesme-makinesi', @RootId_5, 1),
(N'Hesap Makineleri', @SubId_5_0, N'hesap-makineleri', @RootId_5, 1),
(N'Kağıt İmha Makineleri', @SubId_5_0, N'kagit-imha-makineleri', @RootId_5, 1),
(N'Kesintisiz Güç Kaynakları', @SubId_5_0, N'kesintisiz-guc-kaynaklari', @RootId_5, 1),
(N'Laminasyon Makineleri', @SubId_5_0, N'laminasyon-makineleri', @RootId_5, 1),
(N'Para Sayma ve Kontrol Ürünleri', @SubId_5_0, N'para-sayma-ve-kontrol-urunleri', @RootId_5, 1),
(N'Barkod ve Etiket Yazıcı', @SubId_5_0, N'barkod-ve-etiket-yazici', @RootId_5, 1),
(N'El Terminalleri', @SubId_5_0, N'el-terminalleri', @RootId_5, 1);

-- Seviye 1: Oyun Konsolu ve Aksesuarları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Oyun Konsolu ve Aksesuarları', @RootId_5, N'oyun-konsolu-ve-aksesuarlari', @RootId_5, 1);
DECLARE @SubId_5_1 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Oyun Konsolu ve Aksesuarları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'PlayStation', @SubId_5_1, N'playstation', @RootId_5, 1),
(N'Xbox', @SubId_5_1, N'xbox', @RootId_5, 1);

-- Seviye 1: TV, Ses ve Görüntü Sistemleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'TV, Ses ve Görüntü Sistemleri', @RootId_5, N'tv-ses-ve-goruntu-sistemleri', @RootId_5, 1);
DECLARE @SubId_5_2 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (TV, Ses ve Görüntü Sistemleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Televizyon', @SubId_5_2, N'televizyon', @RootId_5, 1),
(N'Televizyon Aksesuarları', @SubId_5_2, N'televizyon-aksesuarlari', @RootId_5, 1),
(N'Projeksiyon Cihazı', @SubId_5_2, N'projeksiyon-cihazi', @RootId_5, 1),
(N'Projeksiyon Aksesuarları', @SubId_5_2, N'projeksiyon-aksesuarlari', @RootId_5, 1),
(N'Hoparlör', @SubId_5_2, N'hoparlor', @RootId_5, 1),
(N'Güvenlik Sistemleri', @SubId_5_2, N'guvenlik-sistemleri', @RootId_5, 1);

-- Seviye 1: Beyaz Eşya
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Beyaz Eşya', @RootId_5, N'beyaz-esya', @RootId_5, 1);
DECLARE @SubId_5_3 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Beyaz Eşya)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Bulaşık Makinesi', @SubId_5_3, N'bulasik-makinesi', @RootId_5, 1),
(N'Buzdolabı', @SubId_5_3, N'buzdolabi', @RootId_5, 1),
(N'Fırın', @SubId_5_3, N'firin', @RootId_5, 1),
(N'Su Sebilleri', @SubId_5_3, N'su-sebilleri', @RootId_5, 1);

-- Seviye 1: Pil
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Pil', @RootId_5, N'pil', @RootId_5, 1);
DECLARE @SubId_5_4 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Pil)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'AA / Kalem Pil', @SubId_5_4, N'aa-kalem-pil', @RootId_5, 1),
(N'AAA / İnce Kalem Pil', @SubId_5_4, N'aaa-ince-kalem-pil', @RootId_5, 1),
(N'D / Büyük Pil', @SubId_5_4, N'd-buyuk-pil', @RootId_5, 1),
(N'C / Orta Pil', @SubId_5_4, N'c-orta-pil', @RootId_5, 1),
(N'Düğme Pil', @SubId_5_4, N'dugme-pil', @RootId_5, 1),
(N'Şarj Edilebilir Pil', @SubId_5_4, N'sarj-edilebilir-pil', @RootId_5, 1),
(N'Pil Şarj Aleti', @SubId_5_4, N'pil-sarj-aleti', @RootId_5, 1);

-- Seviye 1: Kulaklık
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kulaklık', @RootId_5, N'kulaklik', @RootId_5, 1);
DECLARE @SubId_5_5 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kulaklık)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Mikrofonlu Kulaklık', @SubId_5_5, N'mikrofonlu-kulaklik', @RootId_5, 1),
(N'Bluetooth Kulaklık', @SubId_5_5, N'bluetooth-kulaklik', @RootId_5, 1),
(N'Kulak Üstü Kulaklık', @SubId_5_5, N'kulak-ustu-kulaklik', @RootId_5, 1),
(N'Kulak İçi Kulaklık', @SubId_5_5, N'kulak-ici-kulaklik', @RootId_5, 1);

-- Seviye 1: Bilgisayar ve Aksesuarları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Bilgisayar ve Aksesuarları', @RootId_5, N'bilgisayar-ve-aksesuarlari', @RootId_5, 1);
DECLARE @SubId_5_6 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Bilgisayar ve Aksesuarları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Notebook – Laptop', @SubId_5_6, N'notebook-laptop', @RootId_5, 1),
(N'Masaüstü Bilgisayar', @SubId_5_6, N'masaustu-bilgisayar', @RootId_5, 1),
(N'Klavyeler', @SubId_5_6, N'klavyeler', @RootId_5, 1),
(N'Mouse', @SubId_5_6, N'mouse', @RootId_5, 1),
(N'Monitörler', @SubId_5_6, N'monitorler', @RootId_5, 1),
(N'Mouse Pad', @SubId_5_6, N'mouse-pad', @RootId_5, 1),
(N'Notebook Soğutucu ve Yükselticiler', @SubId_5_6, N'notebook-sogutucu-ve-yukselticiler', @RootId_5, 1),
(N'Notebook ve Sırt Çantaları', @SubId_5_6, N'notebook-ve-sirt-cantalari', @RootId_5, 1),
(N'Ekran Temizlik Ürünleri', @SubId_5_6, N'ekran-temizlik-urunleri', @RootId_5, 1),
(N'Tablet', @SubId_5_6, N'tablet', @RootId_5, 1),
(N'Wifi Güçlendirici & Menzil Arttırıcı', @SubId_5_6, N'wifi-guclendirici-ve-menzil-arttirici', @RootId_5, 1),
(N'Notebook Adaptörü', @SubId_5_6, N'notebook-adaptoru', @RootId_5, 1),
(N'Bilgisayar Kılıfı', @SubId_5_6, N'bilgisayar-kilifi', @RootId_5, 1);

-- Seviye 1: Teknik Kablolar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Teknik Kablolar', @RootId_5, N'teknik-kablolar', @RootId_5, 1);
DECLARE @SubId_5_7 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Teknik Kablolar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'HDMI Kablo', @SubId_5_7, N'hdmi-kablo', @RootId_5, 1),
(N'USB Hub Çoklayıcı', @SubId_5_7, N'usb-hub-coklayici', @RootId_5, 1),
(N'Şarj ve Data Kablosu', @SubId_5_7, N'sarj-ve-data-kablosu', @RootId_5, 1);

-- Seviye 1: Veri Depolama Ürünleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Veri Depolama Ürünleri', @RootId_5, N'veri-depolama-urunleri', @RootId_5, 1);
DECLARE @SubId_5_8 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Veri Depolama Ürünleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Boş CD ve DVD''ler', @SubId_5_8, N'bos-cd-ve-dvdler', @RootId_5, 1),
(N'Hafıza Kartları', @SubId_5_8, N'hafiza-kartlari', @RootId_5, 1),
(N'USB Bellekler', @SubId_5_8, N'usb-bellekler', @RootId_5, 1),
(N'Harici Harddiskler', @SubId_5_8, N'harici-harddiskler', @RootId_5, 1);

-- Seviye 1: Telefon
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Telefon', @RootId_5, N'telefon', @RootId_5, 1);
DECLARE @SubId_5_9 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Telefon)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Masaüstü Telefonlar', @SubId_5_9, N'masaustu-telefonlar', @RootId_5, 1),
(N'Telsiz (Dect) Telefonlar', @SubId_5_9, N'telsiz-dect-telefonlar', @RootId_5, 1),
(N'El Telsizleri', @SubId_5_9, N'el-telsizleri', @RootId_5, 1),
(N'IP Telefonlar', @SubId_5_9, N'ip-telefonlar', @RootId_5, 1),
(N'Cep Telefonları', @SubId_5_9, N'cep-telefonlari', @RootId_5, 1);

-- Seviye 1: Telefon Aksesuarları
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Telefon Aksesuarları', @RootId_5, N'telefon-aksesuarlari', @RootId_5, 1);
DECLARE @SubId_5_10 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Telefon Aksesuarları)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Powerbank', @SubId_5_10, N'powerbank', @RootId_5, 1),
(N'Şarj ve Data Kablosu', @SubId_5_10, N'telefon-aksesuarlari-sarj-ve-data-kablosu', @RootId_5, 1),
(N'Şarj Cihazı', @SubId_5_10, N'sarj-cihazi', @RootId_5, 1),
(N'Telefon Kılıfı', @SubId_5_10, N'telefon-kilifi', @RootId_5, 1);

-- Seviye 1: Yazıcılar
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Yazıcılar', @RootId_5, N'yazicilar', @RootId_5, 1);
DECLARE @SubId_5_11 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Yazıcılar)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Barkod ve Etiket Yazıcı', @SubId_5_11, N'yazicilar-barkod-ve-etiket-yazici', @RootId_5, 1),
(N'Lazer Yazıcılar', @SubId_5_11, N'lazer-yazicilar', @RootId_5, 1),
(N'Mürekkep Püskürtmeli Yazıcılar', @SubId_5_11, N'murekkep-puskurtmeli-yazicilar', @RootId_5, 1),
(N'Nokta Vuruşlu Yazıcılar', @SubId_5_11, N'nokta-vuruslu-yazicilar', @RootId_5, 1),
(N'Tarayıcı', @SubId_5_11, N'tarayici', @RootId_5, 1),
(N'3D Yazıcılar', @SubId_5_11, N'3d-yazicilar', @RootId_5, 1);

-- Seviye 1: Küçük Ev Aletleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Küçük Ev Aletleri', @RootId_5, N'kucuk-ev-aletleri', @RootId_5, 1);
DECLARE @SubId_5_12 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Küçük Ev Aletleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Çay Makinesi', @SubId_5_12, N'cay-makinesi', @RootId_5, 1),
(N'Çay Otomatı', @SubId_5_12, N'cay-otomati', @RootId_5, 1),
(N'Su Isıtıcıları', @SubId_5_12, N'su-isiticilari', @RootId_5, 1),
(N'Su Sebilleri', @SubId_5_12, N'kucuk-ev-aletleri-su-sebilleri', @RootId_5, 1),
(N'Tost Makinesi', @SubId_5_12, N'tost-makinesi', @RootId_5, 1),
(N'Fırın', @SubId_5_12, N'kucuk-ev-aletleri-firin', @RootId_5, 1),
(N'Elektrikli Ocak', @SubId_5_12, N'elektrikli-ocak', @RootId_5, 1),
(N'Buzdolabı', @SubId_5_12, N'kucuk-ev-aletleri-buzdolabi', @RootId_5, 1),
(N'Saç Kurutma Makinesi', @SubId_5_12, N'sac-kurutma-makinesi', @RootId_5, 1),
(N'Ütü', @SubId_5_12, N'utu', @RootId_5, 1),
(N'Elektrikli Süpürge', @SubId_5_12, N'elektrikli-supurge', @RootId_5, 1);

-- Seviye 1: Kahve Makineleri
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'Kahve Makineleri', @RootId_5, N'kahve-makineleri', @RootId_5, 1);
DECLARE @SubId_5_13 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (Kahve Makineleri)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Türk Kahvesi Makinesi', @SubId_5_13, N'turk-kahvesi-makinesi', @RootId_5, 1),
(N'Otomatik Kahve Makinesi', @SubId_5_13, N'otomatik-kahve-makinesi', @RootId_5, 1),
(N'Manuel Espresso Makinesi', @SubId_5_13, N'manuel-espresso-makinesi', @RootId_5, 1),
(N'Elektrikli Cezve', @SubId_5_13, N'elektrikli-cezve', @RootId_5, 1);

-- Seviye 1: İklimlendirme
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive)
VALUES (N'İklimlendirme', @RootId_5, N'iklimlendirme', @RootId_5, 1);
DECLARE @SubId_5_14 INT = SCOPE_IDENTITY();

-- Seviye 2 Yapraklar (İklimlendirme)
INSERT INTO dbo.Categories (Name, ParentCategoryId, Slug, SectorId, IsActive) VALUES
(N'Isıtıcılar', @SubId_5_14, N'isiticilar', @RootId_5, 1),
(N'Vantilatörler', @SubId_5_14, N'vantilatorler', @RootId_5, 1),
(N'Klimalar', @SubId_5_14, N'klimalar', @RootId_5, 1),
(N'Hava Temizleme Cihazı', @SubId_5_14, N'hava-temizleme-cihazi', @RootId_5, 1);

GO

-- ============================================================================
-- SEVİYE VE HİYERARŞİ YOLLARINI GÜNCELLE (Materialized Path & Depth)
-- ============================================================================
WITH CatPaths AS (
    SELECT Id, CAST(Slug AS NVARCHAR(1000)) AS FullPath, 0 AS TreeDepth
    FROM dbo.Categories
    WHERE ParentCategoryId IS NULL
    UNION ALL
    SELECT c.Id, CAST(cp.FullPath + '/' + c.Slug AS NVARCHAR(1000)), cp.TreeDepth + 1
    FROM dbo.Categories c
    JOIN CatPaths cp ON c.ParentCategoryId = cp.Id
)
UPDATE c
SET c.CategoryPath = cp.FullPath,
    c.Depth = cp.TreeDepth
FROM dbo.Categories c
JOIN CatPaths cp ON c.Id = cp.Id;
GO

PRINT 'mutlak-kategori.pdf kaynaklı kategori ağacı ve hiyerarşi yolları başarıyla yüklendi.';
GO

-- ============================================================================
-- TUTUMLU - KATEGORİ BAZLI FİLTRE & ÖZNİTELİK TANIMLARI (SEED DOKÜMANI)
-- ============================================================================
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

-- 2. SEED VERİLERİ (6 Ana Sektör İçin Öznitelik Tanımları)

-- Sektör 3: GIDA VE MUTFAK
MERGE INTO dbo.CategoryAttributes AS target
USING (VALUES
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Aroma', 'Aroma / Tat', 1, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Şeker / Diyet', 'Şeker & Diyet Profili', 2, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Hacim', 'Hacim (ml / L)', 3, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Ağırlık', 'Ağırlık (g / kg)', 4, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Paket Adedi', 'Paket Adedi (Multipack)', 5, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Ambalaj', 'Ambalaj Türü (Kutu / Cam / Pet)', 6, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'İçim Stili', 'İçim / Kavrum Stili', 7, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'gida-ve-mutfak' AND ParentCategoryId IS NULL), NULL, 'Form', 'Ürün Formu (Toz / Sıvı)', 8, 1)
) AS source (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
ON (target.SectorId = source.SectorId AND target.CategoryId IS NULL AND target.AttributeName = source.AttributeName)
WHEN MATCHED THEN
    UPDATE SET target.DisplayName = source.DisplayName, target.DisplayOrder = source.DisplayOrder, target.IsFilterable = source.IsFilterable
WHEN NOT MATCHED THEN
    INSERT (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
    VALUES (source.SectorId, source.CategoryId, source.AttributeName, source.DisplayName, source.DisplayOrder, source.IsFilterable);
GO

-- Sektör 6: TEKNOLOJİ
MERGE INTO dbo.CategoryAttributes AS target
USING (VALUES
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'teknoloji' AND ParentCategoryId IS NULL), NULL, 'Dahili Hafıza', 'Dahili Hafıza (Depolama)', 1, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'teknoloji' AND ParentCategoryId IS NULL), NULL, 'RAM', 'Bellek (RAM)', 2, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'teknoloji' AND ParentCategoryId IS NULL), NULL, 'Renk', 'Renk', 3, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'teknoloji' AND ParentCategoryId IS NULL), NULL, 'Seri', 'Model Serisi', 4, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'teknoloji' AND ParentCategoryId IS NULL), NULL, 'Boyut', 'Ekran / Kasa Boyutu', 5, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'teknoloji' AND ParentCategoryId IS NULL), NULL, 'Bağlantı', 'Bağlantı Türü (Wi-Fi / 5G / BT)', 6, 1)
) AS source (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
ON (target.SectorId = source.SectorId AND target.CategoryId IS NULL AND target.AttributeName = source.AttributeName)
WHEN MATCHED THEN
    UPDATE SET target.DisplayName = source.DisplayName, target.DisplayOrder = source.DisplayOrder, target.IsFilterable = source.IsFilterable
WHEN NOT MATCHED THEN
    INSERT (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
    VALUES (source.SectorId, source.CategoryId, source.AttributeName, source.DisplayName, source.DisplayOrder, source.IsFilterable);
GO

-- Sektör 2: TEMİZLİK VE SAĞLIK
MERGE INTO dbo.CategoryAttributes AS target
USING (VALUES
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'temizlik-ve-saglik' AND ParentCategoryId IS NULL), NULL, 'Form', 'Ürün Formu (Sıvı / Toz / Tablet / Jel)', 1, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'temizlik-ve-saglik' AND ParentCategoryId IS NULL), NULL, 'Aroma', 'Koku / Ferahlık Varyantı', 2, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'temizlik-ve-saglik' AND ParentCategoryId IS NULL), NULL, 'Hacim', 'Hacim (ml / L)', 3, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'temizlik-ve-saglik' AND ParentCategoryId IS NULL), NULL, 'Ağırlık', 'Ağırlık (g / kg)', 4, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'temizlik-ve-saglik' AND ParentCategoryId IS NULL), NULL, 'Paket Adedi', 'Paket Adedi', 5, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'temizlik-ve-saglik' AND ParentCategoryId IS NULL), NULL, 'Ambalaj', 'Ambalaj / Şişe Türü', 6, 1)
) AS source (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
ON (target.SectorId = source.SectorId AND target.CategoryId IS NULL AND target.AttributeName = source.AttributeName)
WHEN MATCHED THEN
    UPDATE SET target.DisplayName = source.DisplayName, target.DisplayOrder = source.DisplayOrder, target.IsFilterable = source.IsFilterable
WHEN NOT MATCHED THEN
    INSERT (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
    VALUES (source.SectorId, source.CategoryId, source.AttributeName, source.DisplayName, source.DisplayOrder, source.IsFilterable);
GO

-- Sektör 5: MOBİLYA VE HIRDAVAT
MERGE INTO dbo.CategoryAttributes AS target
USING (VALUES
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'mobilya-ve-hirdavat' AND ParentCategoryId IS NULL), NULL, 'Malzeme', 'Malzeme / Materyal', 1, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'mobilya-ve-hirdavat' AND ParentCategoryId IS NULL), NULL, 'Renk', 'Renk', 2, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'mobilya-ve-hirdavat' AND ParentCategoryId IS NULL), NULL, 'Seri', 'Koleksiyon / Seri', 3, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'mobilya-ve-hirdavat' AND ParentCategoryId IS NULL), NULL, 'Boyut', 'Ölçü / Boyut', 4, 1)
) AS source (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
ON (target.SectorId = source.SectorId AND target.CategoryId IS NULL AND target.AttributeName = source.AttributeName)
WHEN MATCHED THEN
    UPDATE SET target.DisplayName = source.DisplayName, target.DisplayOrder = source.DisplayOrder, target.IsFilterable = source.IsFilterable
WHEN NOT MATCHED THEN
    INSERT (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
    VALUES (source.SectorId, source.CategoryId, source.AttributeName, source.DisplayName, source.DisplayOrder, source.IsFilterable);
GO

-- Sektör 1: OFİS KIRTASİYE & KAĞIT ÜRÜNLERİ
MERGE INTO dbo.CategoryAttributes AS target
USING (VALUES
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'ofis-kirtasiye-ve-kagit-urunleri' AND ParentCategoryId IS NULL), NULL, 'Renk', 'Mürekkep / Ürün Rengi', 1, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'ofis-kirtasiye-ve-kagit-urunleri' AND ParentCategoryId IS NULL), NULL, 'Uç Kalınlığı', 'Uç Kalınlığı (0.5, 0.7 vb.)', 2, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'ofis-kirtasiye-ve-kagit-urunleri' AND ParentCategoryId IS NULL), NULL, 'Boyut', 'Kağıt / Defter Boyutu (A4, A5)', 3, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'ofis-kirtasiye-ve-kagit-urunleri' AND ParentCategoryId IS NULL), NULL, 'Ağırlık', 'Gramaj (80g, 90g vb.)', 4, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'ofis-kirtasiye-ve-kagit-urunleri' AND ParentCategoryId IS NULL), NULL, 'Paket Adedi', 'Paket / Koli Adedi', 5, 1)
) AS source (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
ON (target.SectorId = source.SectorId AND target.CategoryId IS NULL AND target.AttributeName = source.AttributeName)
WHEN MATCHED THEN
    UPDATE SET target.DisplayName = source.DisplayName, target.DisplayOrder = source.DisplayOrder, target.IsFilterable = source.IsFilterable
WHEN NOT MATCHED THEN
    INSERT (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
    VALUES (source.SectorId, source.CategoryId, source.AttributeName, source.DisplayName, source.DisplayOrder, source.IsFilterable);
GO

-- Sektör 4: KARTUŞ - TONER
MERGE INTO dbo.CategoryAttributes AS target
USING (VALUES
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'kartus-toner' AND ParentCategoryId IS NULL), NULL, 'Renk', 'Baskı Rengi (Black / Cyan / Magenta / Yellow)', 1, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'kartus-toner' AND ParentCategoryId IS NULL), NULL, 'Uyumlu Marka', 'Uyumlu Yazıcı Markası', 2, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'kartus-toner' AND ParentCategoryId IS NULL), NULL, 'Kapasite', 'Baskı Kapasitesi (Standart / Yüksek XL)', 3, 1),
    ((SELECT Id FROM dbo.Categories WHERE Slug = 'kartus-toner' AND ParentCategoryId IS NULL), NULL, 'Paket Adedi', 'Paket Adedi', 4, 1)
) AS source (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
ON (target.SectorId = source.SectorId AND target.CategoryId IS NULL AND target.AttributeName = source.AttributeName)
WHEN MATCHED THEN
    UPDATE SET target.DisplayName = source.DisplayName, target.DisplayOrder = source.DisplayOrder, target.IsFilterable = source.IsFilterable
WHEN NOT MATCHED THEN
    INSERT (SectorId, CategoryId, AttributeName, DisplayName, DisplayOrder, IsFilterable)
    VALUES (source.SectorId, source.CategoryId, source.AttributeName, source.DisplayName, source.DisplayOrder, source.IsFilterable);
GO

PRINT 'CategoryAttributes tanımları ve seed verileri başarıyla yüklendi.';
GO
