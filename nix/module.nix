{ network-master-server }:
{
  lib,
  config,
  ...
}:
let
  cfg = config.services.network-master-server;
in
{
  options.services.network-master-server = {
    enable = lib.mkEnableOption "network master server";

    package = lib.mkOption {
      type = lib.types.package;
      default = network-master-server."x86_64-linux".default;
    };

    user = lib.mkOption {
      type = lib.types.str;
      default = "network-master-server";
    };

    group = lib.mkOption {
      type = lib.types.str;
      default = "network-master-server";
    };

    port = lib.mkOption {
      type = lib.types.port;
      default = 5312;
    };
  };

  config = lib.mkIf cfg.enable {
    users = {
      groups = lib.mkIf (cfg.group == "network-master-server") {
        network-master-server = { };
      };

      users = lib.mkIf (cfg.user == "network-master-server") {
        network-master-server = {
          group = cfg.group;
          isSystemUser = true;
        };
      };
    };
    systemd.services.network-master-server = {
      wantedBy = [ "multi-user.target" ];
      after = [ "network.target" ];
      serviceConfig = {
        User = cfg.user;
        Group = cfg.group;
        ExecStart = "${lib.getExe cfg.package} -port ${toString cfg.port}";
      };
    };
  };
}
