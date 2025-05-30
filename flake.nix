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
          vendorHash = "sha256-IZHfSi/vU4Kr2XYd9DKINsyF5hMw+xKZWF8jAmzjKgk=";
          meta.mainProgram = "network-master-server";
        };
      };

      nixosModules = {
        network-master-server = import ./nix/module.nix { network-master-server = self.packages; };
      };
    };
}
