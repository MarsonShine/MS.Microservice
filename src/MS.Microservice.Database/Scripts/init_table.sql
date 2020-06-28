  IF EXISTS(SELECT 1 FROM information_schema.tables 
  WHERE table_name = '
'__EFMigrationsHistory'' AND table_schema = DATABASE()) 
BEGIN
CREATE TABLE `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

END;

CREATE TABLE `tb_orders` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `IsDelete` bit NOT NULL,
    `CreationTime` timestamp NOT NULL DEFAULT '2020-06-28 13:05:38.418881',
    `OrderNumber` varchar(25) NOT NULL,
    `OrderName` varchar(255) NOT NULL,
    `Price` decimal(5,3) NOT NULL,
    `UpdationTime` timestamp NULL,
    PRIMARY KEY (`Id`)
);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20200628050538_InitDatabaseCreate', '3.1.5');

