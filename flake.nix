{
  inputs.nixpkgs.url = "github:nixos/nixpkgs/nixpkgs-unstable";

  outputs =
    { self, nixpkgs }:
    let
      pkgs = import nixpkgs { system = "x86_64-linux"; };
    in
    {
      devShells."x86_64-linux".default = pkgs.mkShell {
        packages = with pkgs; [
          go
        ];
      };

      packages."x86_64-linux" = rec {
        default = network-master-server;
        network-master-server = pkgs.buildGoModule {
          pname = "network-master-server";
          version = "0.0.0";
          src = ./.;
          vendorHash = "sha256-mGKxBRU5TPgdmiSx0DHEd0Ys8gsVD/YdBfbDdSVpC3U=";
          meta.mainProgram = "network-master-server";
        };
      };

      nixosModules = {
        network-master-server = import ./nix/module.nix { network-master-server = self.packages; };
      };
    };
}
