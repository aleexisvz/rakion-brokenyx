-- =============================================================================
-- rakion-brokenyx: Clean-Room Database Schema
-- Compatibility: MySQL 8.0+ / MariaDB 10.5+
-- Storage Engine: InnoDB (Full ACID transactions, foreign keys, row-level locks)
-- Character Set: utf8mb4 / Collation: utf8mb4_unicode_ci
-- =============================================================================

SET FOREIGN_KEY_CHECKS = 0;

-- -----------------------------------------------------------------------------
-- 1. Users and Authentication
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `users`;
CREATE TABLE `users` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `username` VARCHAR(16) NOT NULL,
    `password_hash` VARCHAR(255) NOT NULL,
    `email` VARCHAR(100) NOT NULL DEFAULT '',
    `country` SMALLINT NOT NULL DEFAULT 0,
    `no_country_update` TINYINT(1) NOT NULL DEFAULT 0,
    `authority` INT NOT NULL DEFAULT 0,
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `updated_at` DATETIME NULL ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_users_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 2. User Game Progression & Attributes
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `user_game_info`;
CREATE TABLE `user_game_info` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `user_id` INT NOT NULL,
    `username` VARCHAR(16) NOT NULL,
    `gold` BIGINT NOT NULL DEFAULT 10000,
    `tutorial_completed` TINYINT(1) NOT NULL DEFAULT 0,
    `stage_level_free` TINYINT(1) NOT NULL DEFAULT 0,
    `slot_count` TINYINT UNSIGNED NOT NULL DEFAULT 3,
    `bag_count` TINYINT UNSIGNED NOT NULL DEFAULT 1,
    `is_banned` TINYINT(1) NOT NULL DEFAULT 0,
    `ban_date` DATETIME NULL,
    `ban_reason` VARCHAR(64) NULL,
    `clan_id` INT NOT NULL DEFAULT 0,
    `clan_point` INT NOT NULL DEFAULT 0,
    `clan_rank` INT NOT NULL DEFAULT 0,
    `clan_grade` INT NOT NULL DEFAULT 0,
    `buddy_name` VARCHAR(16) NULL,
    `power_time` BIGINT NOT NULL DEFAULT 0,
    `power_level_point` INT NOT NULL DEFAULT 0,
    `power_time_date` DATETIME NULL,
    `active_character_name` VARCHAR(32) NULL,
    `rank_grade` SMALLINT NOT NULL DEFAULT 0,
    `country` INT NOT NULL DEFAULT 9,
    `last_connect` DATETIME NULL,
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_user_game_info_user_id` (`user_id`),
    KEY `idx_user_game_info_username` (`username`),
    CONSTRAINT `fk_user_game_info_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 3. Cash Balances
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `cash_balances`;
CREATE TABLE `cash_balances` (
    `user_id` INT NOT NULL,
    `username` VARCHAR(16) NOT NULL,
    `cash_amount` INT NOT NULL DEFAULT 0,
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`user_id`),
    CONSTRAINT `fk_cash_balances_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 4. Characters
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `characters`;
CREATE TABLE `characters` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `user_id` INT NOT NULL,
    `name` VARCHAR(12) NOT NULL,
    `is_used` TINYINT(1) NOT NULL DEFAULT 1,
    `delete_key` VARCHAR(50) NOT NULL DEFAULT 'sirmaster',
    `auth` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `class_id` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0=Swordman, 1=Archer, 2=Blacksmith, 3=Mage, 4=Ninja',
    `level` TINYINT UNSIGNED NOT NULL DEFAULT 1,
    `win_count` INT NOT NULL DEFAULT 0,
    `loss_count` INT NOT NULL DEFAULT 0,
    `draw_count` INT NOT NULL DEFAULT 0,
    `exp` INT NOT NULL DEFAULT 0,
    `level_point` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `slot_index` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `hit1` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `hit2` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `hit3` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `hit4` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `chit` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `hp` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `ap` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `attack_speed` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `speed` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `max_cp` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `rank_grade` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `total_rank` INT NOT NULL DEFAULT 0,
    `class_rank` INT NOT NULL DEFAULT 0,
    `potion_slot_count` TINYINT UNSIGNED NOT NULL DEFAULT 3,
    `change_time` DATETIME NULL ON UPDATE CURRENT_TIMESTAMP,
    `create_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_characters_name` (`name`),
    KEY `idx_characters_user_slot` (`user_id`, `slot_index`),
    CONSTRAINT `fk_characters_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 4b. Level table: experience a character of each class must exceed to leave a level
--     (imported from the client's DataSetup.xfs with --import-levels).
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `class_level_info` (
    `class_id` TINYINT UNSIGNED NOT NULL,
    `level` TINYINT UNSIGNED NOT NULL,
    `exp` INT NOT NULL DEFAULT 0,
    PRIMARY KEY (`class_id`, `level`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 4c. Creature level table: experience a creature item (id 8000 + npc) must exceed to leave a level
--     (legacy NPCInfo; imported from the operator's dump with --import-items).
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `creature_level_info` (
    `npc` TINYINT UNSIGNED NOT NULL,
    `level` TINYINT UNSIGNED NOT NULL,
    `exp` INT NOT NULL DEFAULT 0,
    PRIMARY KEY (`npc`, `level`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 4d. Stage clears: best rank of each character per stage (legacy UserStageInfo).
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `user_stage_info` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `character_id` INT NOT NULL,
    `stage` TINYINT UNSIGNED NOT NULL,
    `rank` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_user_stage_info` (`character_id`, `stage`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Stage level pass (0x0071): the account plays any Stage whatever its level until this minute. Also created on first
-- use by AccountUpgradeRepository.
CREATE TABLE IF NOT EXISTS `user_stage_level_free` (
    `user_id` INT NOT NULL PRIMARY KEY,
    `until_minute` BIGINT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 4e. Ranking snapshots for ranking pages (legacy TotalRankP / <Class>RankP), rebuilt by the ranking job
--     (Rakion.Maintenance); also created on first run.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `ranking_total` (
    `character_id` INT NOT NULL, `rank` INT NOT NULL, `last_rank` INT NOT NULL, `class_rank` INT NOT NULL,
    `country` INT NOT NULL, `username` VARCHAR(16) NOT NULL, `name` VARCHAR(16) NOT NULL, `level` INT NOT NULL,
    `class_id` INT NOT NULL, `grade` INT NOT NULL, `exp` INT NOT NULL, `win` INT NOT NULL, `lose` INT NOT NULL, `draw` INT NOT NULL,
    PRIMARY KEY (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ranking_class` (
    `character_id` INT NOT NULL, `class_id` INT NOT NULL, `rank` INT NOT NULL, `last_rank` INT NOT NULL,
    `country` INT NOT NULL, `username` VARCHAR(16) NOT NULL, `name` VARCHAR(16) NOT NULL, `level` INT NOT NULL,
    `grade` INT NOT NULL, `exp` INT NOT NULL, `win` INT NOT NULL, `lose` INT NOT NULL, `draw` INT NOT NULL,
    PRIMARY KEY (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 5. Item Templates Catalog
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `item_templates`;
CREATE TABLE `item_templates` (
    `id` INT NOT NULL,
    `type_id` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `class_id` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `required_level` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `shop_type` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `gold_price` INT NOT NULL DEFAULT 0,
    `cash_price` INT NOT NULL DEFAULT 0,
    `hit1` INT NOT NULL DEFAULT 0,
    `hit2` INT NOT NULL DEFAULT 0,
    `hit3` INT NOT NULL DEFAULT 0,
    `hit4` INT NOT NULL DEFAULT 0,
    `chit` INT NOT NULL DEFAULT 0,
    `ap` INT NOT NULL DEFAULT 0,
    `hp` INT NOT NULL DEFAULT 0,
    `max_cp` INT NOT NULL DEFAULT 0,
    `power` INT NOT NULL DEFAULT 0,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 6. User Items (Inventory, Equipment & Creature Instances)
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `user_items`;
CREATE TABLE `user_items` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `user_id` INT NOT NULL,
    `character_id` INT NOT NULL DEFAULT 0,
    `item_id` INT NOT NULL,
    `serial_number` INT NOT NULL DEFAULT 8000,
    `serial_type` TINYINT UNSIGNED NOT NULL DEFAULT 3,
    `level` TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Enchant refine level',
    `limit_time_minutes` INT NOT NULL DEFAULT 0 COMMENT '0 or negative = permanent',
    `slot_index` TINYINT UNSIGNED NOT NULL DEFAULT 1,
    `exp` BIGINT NOT NULL DEFAULT 0 COMMENT 'Creature EXP',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    KEY `idx_user_items_user_char` (`user_id`, `character_id`),
    CONSTRAINT `fk_user_items_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_user_items_template` FOREIGN KEY (`item_id`) REFERENCES `item_templates` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 7. Clans / Guilds
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `clans`;
CREATE TABLE `clans` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `master_id` INT NOT NULL,
    `master_name` VARCHAR(16) NOT NULL,
    `name` VARCHAR(16) NOT NULL,
    `points` INT NOT NULL DEFAULT 0,
    `member_count` SMALLINT NOT NULL DEFAULT 1,
    `rank` INT UNSIGNED NOT NULL DEFAULT 0,
    `country` SMALLINT NOT NULL DEFAULT 0,
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_clans_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 8. Buddies (Friend List)
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS `buddies`;
CREATE TABLE `buddies` (
    `user_id` VARCHAR(16) NOT NULL,
    `category` VARCHAR(20) NOT NULL DEFAULT '',
    `buddy_user_id` VARCHAR(16) NOT NULL,
    PRIMARY KEY (`user_id`, `buddy_user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;

